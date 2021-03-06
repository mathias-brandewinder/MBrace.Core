﻿namespace MBrace

open System
open System.Runtime.Serialization
open System.IO
open System.Text

open MBrace
open MBrace.Store
open MBrace.Continuation

#nowarn "444"

/// Represents an immutable reference to an
/// object that is persisted in the underlying store.
/// Cloud cells cached locally for performance.
[<Sealed; DataContract; StructuredFormatDisplay("{StructuredFormatDisplay}")>]
type CloudCell<'T> =

    // https://visualfsharp.codeplex.com/workitem/199
    [<DataMember(Name = "Path")>]
    val mutable private path : string
    [<DataMember(Name = "UUID")>]
    val mutable private uuid : string
    [<DataMember(Name = "Deserializer")>]
    val mutable private deserializer : (Stream -> 'T) option
    [<DataMember(Name = "CacheByDefault")>]
    val mutable private cacheByDefault : bool

    internal new (path, deserializer, ?cacheByDefault) =
        let uuid = Guid.NewGuid().ToString()
        let cacheByDefault = defaultArg cacheByDefault false
        { uuid = uuid ; path = path ; deserializer = deserializer ; cacheByDefault = cacheByDefault }

    /// Path to cloud cell payload in store
    member c.Path = c.path

    /// Enables or disables implicit, on-demand caching of values when first dereferenced.
    member c.CacheByDefault
        with get () = c.cacheByDefault
        and set cbd = c.cacheByDefault <- cbd

    interface ICloudCacheable<'T> with
        member c.UUID = c.uuid
        member c.GetSourceValue() = local {
            let! config = Cloud.GetResource<CloudFileStoreConfiguration>()
            use! stream = ofAsync <| config.FileStore.BeginRead c.path
            match c.deserializer with 
            | Some ds -> return ds stream
            | None -> 
                let! defaultSerializer = Cloud.GetResource<ISerializer> ()
                return defaultSerializer.Deserialize<'T>(stream, leaveOpen = false)
        }

    /// Dereference the cloud cell
    member c.Value = local { return! CloudCache.GetCachedValue(c, cacheIfNotExists = c.CacheByDefault) }

    /// Caches the cloud cell value to the local execution contexts. Returns true iff successful.
    member c.ForceCache() = local { return! CloudCache.PopulateLocalCache c }

    /// Indicates if array is cached in local execution context
    member c.IsCachedLocally = local { return! CloudCache.IsCachedLocally c }

    /// Gets the size of cloud cell in bytes
    member c.Size = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration>()
        return! ofAsync <| config.FileStore.GetFileSize c.path
    }

    override c.ToString() = sprintf "CloudCell[%O] at %s" typeof<'T> c.path
    member private c.StructuredFormatDisplay = c.ToString()

    interface ICloudDisposable with
        member c.Dispose () = local {
            let! config = Cloud.GetResource<CloudFileStoreConfiguration>()
            return! ofAsync <| config.FileStore.DeleteFile c.path
        }

    interface ICloudStorageEntity with
        member c.Type = sprintf "cloudref:%O" typeof<'T>
        member c.Id = c.path

#nowarn "444"

type CloudCell =
    
    /// <summary>
    ///     Creates a new local reference to the underlying store with provided value.
    ///     Cloud cells are immutable and cached locally for performance.
    /// </summary>
    /// <param name="value">Cloud cell value.</param>
    /// <param name="directory">FileStore directory used for cloud cell. Defaults to execution context setting.</param>
    /// <param name="serializer">Serializer used for object serialization. Defaults to runtime context.</param>
    /// <param name="cacheByDefault">Enables implicit, on-demand caching of cell value across instances. Defaults to false.</param>
    static member New(value : 'T, ?directory : string, ?serializer : ISerializer, ?cacheByDefault : bool) = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration>()
        let directory = defaultArg directory config.DefaultDirectory
        let! _serializer = local {
            match serializer with 
            | Some s -> return s 
            | None -> return! Cloud.GetResource<ISerializer>()
        }

        let deserializer = serializer |> Option.map (fun ser stream -> ser.Deserialize<'T>(stream, leaveOpen = false))
        let path = config.FileStore.GetRandomFilePath directory
        let writer (stream : Stream) = async {
            // write value
            _serializer.Serialize(stream, value, leaveOpen = false)
        }
        do! ofAsync <| config.FileStore.Write(path, writer)
        return new CloudCell<'T>(path, deserializer, ?cacheByDefault = cacheByDefault)
    }

    /// <summary>
    ///     Creates a CloudCell from file path with user-provided deserialization function.
    /// </summary>
    /// <param name="path">Path to file.</param>
    /// <param name="deserializer">Value deserializer function.</param>
    /// <param name="force">Force evaluation. Defaults to false.</param>
    /// <param name="cacheByDefault">Enable caching by default on every node where cell is dereferenced. Defaults to false.</param>
    static member FromFile<'T>(path : string, ?deserializer : Stream -> 'T, ?force : bool, ?cacheByDefault : bool) : Local<CloudCell<'T>> = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration>()
        let cell = new CloudCell<'T>(path, deserializer, ?cacheByDefault = cacheByDefault)
        if defaultArg force false then
            let! _ = cell.ForceCache() in ()
        else
            let! exists = ofAsync <| config.FileStore.FileExists path
            if not exists then return raise <| new FileNotFoundException(path)
        return cell
    }

    /// <summary>
    ///     Creates a cloud cell of given type with provided serializer. If successful, returns the cloud cell instance.
    /// </summary>
    /// <param name="path">Path to cloud cell.</param>
    /// <param name="serializer">Serializer for cloud cell.</param>
    /// <param name="cacheByDefault">Enable caching by default on every node where cell is dereferenced. Defaults to false.</param>
    static member FromFile<'T>(path : string, ?serializer : ISerializer, ?force : bool, ?cacheByDefault : bool) = local {
        let deserializer = serializer |> Option.map (fun ser stream -> ser.Deserialize<'T>(stream, leaveOpen = false))
        return! CloudCell.FromFile<'T>(path, ?deserializer = deserializer, ?force = force, ?cacheByDefault = cacheByDefault)
    }

    /// <summary>
    ///     Creates a CloudCell parsing a text file.
    /// </summary>
    /// <param name="path">Path to file.</param>
    /// <param name="textDeserializer">Text deserializer function.</param>
    /// <param name="encoding">Text encoding. Defaults to UTF8.</param>
    /// <param name="force">Force evaluation. Defaults to false.</param>
    /// <param name="cacheByDefault">Enable caching by default on every node where cell is dereferenced. Defaults to false.</param>
    static member FromTextFile<'T>(path : string, textDeserializer : StreamReader -> 'T, ?encoding : Encoding, ?force : bool, ?cacheByDefault) : Local<CloudCell<'T>> = local {
        let deserializer (stream : Stream) =
            let sr =
                match encoding with
                | None -> new StreamReader(stream)
                | Some e -> new StreamReader(stream, e)

            textDeserializer sr 

        return! CloudCell.FromFile(path, deserializer, ?force = force, ?cacheByDefault = cacheByDefault)
    }

    /// <summary>
    ///     Dereference a Cloud cell.
    /// </summary>
    /// <param name="cloudCell">CloudCell to be dereferenced.</param>
    static member Read(cloudCell : CloudCell<'T>) : Local<'T> = cloudCell.Value

    /// <summary>
    ///     Cache a cloud cell to local execution context.
    /// </summary>
    /// <param name="cloudCell">Cloud cell input.</param>
    static member Cache(cloudCell : CloudCell<'T>) : Local<bool> = local { return! cloudCell.ForceCache() }

    /// <summary>
    ///     Disposes cloud cell from store.
    /// </summary>
    /// <param name="cloudCell">Cloud cell to be deleted.</param>
    static member Dispose(cloudCell : CloudCell<'T>) : Local<unit> = local { return! (cloudCell :> ICloudDisposable).Dispose() }