﻿namespace MBrace.Store.Internals

open System
open System.IO

open MBrace.Core
open MBrace.Core.Internals

type ETag = string

/// Cloud file storage abstraction
type ICloudFileStore =

    /// Implementation name
    abstract Name : string

    /// Store identifier
    abstract Id : string

    //
    //  Region : Path operations
    //

    /// Returns the root directory for cloud store instance.
    abstract GetRootDirectory : unit -> string

    /// Generates a random, uniquely specified path to directory
    abstract GetRandomDirectoryName : unit -> string

    /// <summary>
    ///     Returns a normal form for path. Returns None if invalid format.
    /// </summary>
    /// <param name="path">Input filepath.</param>
    abstract TryGetFullPath : path:string -> string option

    /// <summary>
    ///     Returns the directory name for given path.
    /// </summary>
    /// <param name="path">Input file path.</param>
    abstract GetDirectoryName : path:string -> string

    /// <summary>
    ///     Returns the file name for given path.
    /// </summary>
    /// <param name="path">Input file path.</param>
    abstract GetFileName : path:string -> string

    /// <summary>
    ///     Combines an array of paths into a path.
    /// </summary>
    /// <param name="paths">Strings to be combined.</param>
    abstract Combine : paths:string [] -> string

    //
    //  Region : File/Directory operations
    //

    /// <summary>
    ///     Returns the file size in bytes.
    /// </summary>
    /// <param name="path">Path to file.</param>
    abstract GetFileSize : path:string -> Async<int64>

    /// <summary>
    ///     Checks if file exists in store.
    /// </summary>
    /// <param name="path">Path to file.</param>
    abstract FileExists : path:string -> Async<bool>

    /// <summary>
    ///     Gets all files that exist in given container.
    /// </summary>
    /// <param name="directory">Path to directory.</param>
    abstract EnumerateFiles : directory:string -> Async<string []>

    /// <summary>
    ///     Deletes file in given path.
    /// </summary>
    /// <param name="path">File path.</param>
    abstract DeleteFile : path:string -> Async<unit>

    /// <summary>
    ///     Checks if directory exists in given path.
    /// </summary>
    /// <param name="directory">Path to directory.</param>
    abstract DirectoryExists : directory:string -> Async<bool>

    /// <summary>
    ///     Creates a new directory in store.
    /// </summary>
    /// <param name="directory">Path to directory</param>
    abstract CreateDirectory : directory:string -> Async<unit>
        
    /// <summary>
    ///     Deletes provided directory.
    /// </summary>
    /// <param name="directory">file container.</param>
    /// <param name="recursive">Delete recursively.</param>
    abstract DeleteDirectory : directory:string * recursiveDelete:bool -> Async<unit>

    /// <summary>
    ///     Get all directories that exist in given directory.
    /// </summary>
    /// <param name="directory">Directory to enumerate.</param>
    abstract EnumerateDirectories : directory:string -> Async<string []>

    //
    //  Region : File read/write API
    //

    /// <summary>
    ///     Creates a new file in store. If successful returns a writer stream.
    /// </summary>
    /// <param name="path">Path to new file.</param>
    abstract BeginWrite : path:string -> Async<Stream>

    /// <summary>
    ///     Reads from an existing file in store. If successful returns a reader stream.
    /// </summary>
    /// <param name="path">Path to existing file.</param>
    abstract BeginRead : path:string -> Async<Stream>

    /// <summary>
    ///     Creates a new file from provided stream.
    /// </summary>
    /// <param name="targetFile">Target file.</param>
    /// <param name="source">Source stream.</param>
    abstract CopyOfStream : source:Stream * target:string -> Async<unit>

    /// <summary>
    ///     Reads an existing file to target stream.
    /// </summary>
    /// <param name="sourceFile">Source file.</param>
    /// <param name="target">Target stream.</param>
    abstract CopyToStream : sourceFile:string * target:Stream -> Async<unit>

    //
    //  Entity tag API
    //

    /// <summary>
    ///     Asynchronously returns the ETag for provided file, if it exists.
    /// </summary>
    /// <param name="path">Path to file.</param>
    abstract TryGetETag : path:string -> Async<ETag option>

    /// <summary>
    ///     Creates a new file in store. If successful returns a writer stream.
    /// </summary>
    /// <param name="path">Path to new file.</param>
    /// <param name="writer">Asynchronous writer function.</param>
    /// <returns>Returns the write result and the etag of written file.</returns>
    abstract WriteETag : path:string * writer:(Stream -> Async<'R>) -> Async<ETag * 'R>

    /// <summary>
    ///     Attempts to begin reading file from given path,
    ///     provided that supplied etag matches payload.
    /// </summary>
    /// <param name="path">Path to file.</param>
    /// <param name="etag">ETag to be matched.</param>
    /// <returns>Some reader stream if etag matches, or None if it doesn't.</returns>
    abstract ReadETag : path:string * etag:ETag -> Async<Stream option>


/// Cloud storage entity identifier
type ICloudStorageEntity =
    inherit ICloudDisposable
    /// Type identifier for entity
    abstract Type : string
    /// Entity unique identifier
    abstract Id : string

/// Store configuration passed to the continuation execution context
[<NoEquality; NoComparison>]
type CloudFileStoreConfiguration = 
    {
        /// File store.
        FileStore : ICloudFileStore
        /// Default directory used by current execution context.
        DefaultDirectory : string
    }
with
    /// <summary>
    ///     Creates a store configuration instance using provided components.
    /// </summary>
    /// <param name="fileStore">File store instance.</param>
    /// <param name="serializer">Serializer instance.</param>
    /// <param name="defaultDirectory">Default directory for current process. Defaults to auto generated.</param>
    static member Create(fileStore : ICloudFileStore, ?defaultDirectory : string) =
        {
            FileStore = fileStore
            DefaultDirectory = match defaultDirectory with Some d -> d | None -> fileStore.GetRandomDirectoryName()
        }

[<AutoOpen>]
module CloudFileStoreUtils =
    
    type ICloudFileStore with

        /// <summary>
        ///     Reads file in store with provided deserializer function.
        /// </summary>
        /// <param name="deserializer">Deserializer function.</param>
        /// <param name="path">Path to file.</param>
        member cfs.Read<'T>(deserializer : Stream -> Async<'T>, path : string) = async {
            use! stream = cfs.BeginRead path
            return! deserializer stream
        }

        /// <summary>
        ///     Generates a random path in provided directory.
        /// </summary>
        /// <param name="directory">Container directory.</param>
        member cfs.GetRandomFilePath (directory : string) =
            let fileName = Path.GetRandomFileName()
            cfs.Combine [| directory ; fileName |]

        /// Enumerate all directories inside root folder.
        member cfs.EnumerateRootDirectories () = async {
            let dir = cfs.GetRootDirectory()
            return! cfs.EnumerateDirectories(dir)
        }

        /// Combines two strings into a single path.
        member cfs.Combine(path1 : string, path2 : string) = cfs.Combine [| path1 ; path2 |]
        /// Combines two strings into a single path.
        member cfs.Combine(path1 : string, path2 : string, path3 : string) = cfs.Combine [| path1 ; path2 ; path3 |]

        /// <summary>
        ///     Combines a collection of file names with a given path prefix.
        /// </summary>
        /// <param name="container">Path prefix.</param>
        /// <param name="fileNames">File name collections.</param>
        member cfs.Combine(container : string, fileNames : seq<string>) =
            fileNames |> Seq.map (fun f -> cfs.Combine [|container ; f |]) |> Seq.toArray


namespace MBrace.Store

open System
open System.Runtime.Serialization
open System.Text
open System.Threading.Tasks
open System.IO

open MBrace.Core
open MBrace.Core.Internals
open MBrace.Store.Internals

#nowarn "444"

/// Generic FileStore path utilities
type CloudPath =

    /// <summary>
    ///     Gets the default directory used by the runtime.
    /// </summary>
    static member DefaultDirectory : Local<string> = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        return config.DefaultDirectory
    }

    /// <summary>
    ///     Returns the directory name for given path.
    /// </summary>
    /// <param name="path">Input file path.</param>
    static member GetDirectoryName(path : string) = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        return config.FileStore.GetDirectoryName path
    }

    /// <summary>
    ///     Returns the file name for given path.
    /// </summary>
    /// <param name="path">Input file path.</param>
    static member GetFileName(path : string) = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        return config.FileStore.GetFileName path
    }

    /// <summary>
    ///     Combines two strings into one path.
    /// </summary>
    /// <param name="path1">First path.</param>
    /// <param name="path2">Second path.</param>
    static member Combine(path1 : string, path2 : string) = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        return config.FileStore.Combine [| path1 ; path2 |]
    }

    /// <summary>
    ///     Combines three strings into one path.
    /// </summary>
    /// <param name="path1">First path.</param>
    /// <param name="path2">Second path.</param>
    /// <param name="path3">Third path.</param>
    static member Combine(path1 : string, path2 : string, path3 : string) = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        return config.FileStore.Combine [| path1 ; path2 ; path3 |]
    }

    /// <summary>
    ///     Combines an array of paths into a path.
    /// </summary>
    /// <param name="paths">Strings to be combined.</param>
    static member Combine(paths : string []) = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        return config.FileStore.Combine paths
    }

    /// <summary>
    ///     Combines a collection of file names with provided directory prefix.
    /// </summary>
    /// <param name="directory">Directory prefix path.</param>
    /// <param name="fileNames">File names to be combined.</param>
    static member Combine(directory : string, fileNames : seq<string>) = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        return config.FileStore.Combine(directory, fileNames)
    }

    /// Generates a random, uniquely specified path to directory
    static member GetRandomDirectoryName() = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        return config.FileStore.GetRandomDirectoryName()
    }

    /// <summary>
    ///     Creates a uniquely defined file path for given container.
    /// </summary>
    /// <param name="container">Path to containing directory. Defaults to process directory.</param>
    static member GetRandomFileName(?container : string) : Local<string> = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        let container = match container with Some c -> c | None -> config.DefaultDirectory
        return config.FileStore.GetRandomFilePath(container)
    }

/// Represents a directory found in the cloud store
and [<DataContract; Sealed; StructuredFormatDisplay("{StructuredFormatDisplay}")>] CloudDirectory =

    [<DataMember(Name = "Path")>]
    val mutable private path : string

    /// <summary>
    ///     Defines a reference to a cloud directory. This will not create a directory in the local store.
    /// </summary>
    /// <param name="path">Path to directory.</param>
    new (path : string) = { path = path }
    
    /// Path to directory
    member d.Path = d.path

    interface ICloudDisposable with
        member d.Dispose () = CloudDirectory.Delete(d.Path, recursiveDelete = true)

    interface ICloudStorageEntity with
        member d.Type = "CloudDirectory"
        member d.Id = d.path

    override __.ToString() = __.path
    member private r.StructuredFormatDisplay = r.ToString()

    /// <summary>
    ///     Checks if directory exists in given path
    /// </summary>
    /// <param name="dirPath">Path to directory.</param>
    static member Exists(dirPath : string) : Local<bool> = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        return! ofAsync <| config.FileStore.DirectoryExists dirPath
    }

    /// <summary>
    ///     Creates a new directory in store.
    /// </summary>
    /// <param name="dirPath">Path to newly created directory.</param>
    static member Create(dirPath : string) : Local<CloudDirectory> = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        do! ofAsync <| config.FileStore.CreateDirectory(dirPath)
        return new CloudDirectory(dirPath)
    }

    /// <summary>
    ///     Deletes directory from store.
    /// </summary>
    /// <param name="dirPath">Directory to be deleted.</param>
    /// <param name="recursiveDelete">Delete recursively. Defaults to false.</param>
    static member Delete(dirPath : string, ?recursiveDelete : bool) : Local<unit> = local {
        let recursiveDelete = defaultArg recursiveDelete false
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        return! ofAsync <| config.FileStore.DeleteDirectory(dirPath, recursiveDelete = recursiveDelete)
    }

    /// <summary>
    ///     Enumerates all directories contained in path.
    /// </summary>
    /// <param name="directory">Directory to be enumerated.</param>
    static member Enumerate(dirPath : string) : Local<CloudDirectory []> = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        let! dirs = ofAsync <| config.FileStore.EnumerateDirectories(dirPath)
        return dirs |> Array.map (fun d -> new CloudDirectory(d))
    }

/// Represents a file found in the local store
and [<DataContract; Sealed; StructuredFormatDisplay("{StructuredFormatDisplay}")>] CloudFile =

    [<DataMember(Name = "Path")>]
    val mutable private path : string

    /// <summary>
    ///     Defines a reference to a cloud file. This will not create a file in the local store.
    /// </summary>
    /// <param name="path">Path to file.</param>
    new (path : string) = { path = path }
    
    /// Path to cloud file
    member f.Path = f.path

    /// Gets the size (in bytes) of current file if it exists.
    member f.Size = CloudFile.GetSize f.path

    interface ICloudDisposable with
        member f.Dispose () = CloudFile.Delete f.Path

    interface ICloudStorageEntity with
        member d.Type = "CloudFile"
        member d.Id = d.path

    override __.ToString() = __.path
    member private r.StructuredFormatDisplay = r.ToString()

    /// <summary>
    ///     Gets the size of provided file, in bytes.
    /// </summary>
    /// <param name="path">Input file.</param>
    static member GetSize(path : string) : Local<int64> = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        return! ofAsync <| config.FileStore.GetFileSize path
    }

    /// <summary>
    ///     Checks if file exists in store.
    /// </summary>
    /// <param name="path">Input file.</param>
    static member Exists(path : string) = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        return! ofAsync <| config.FileStore.FileExists path
    }

    /// <summary>
    ///     Deletes file in given path.
    /// </summary>
    /// <param name="path">Input file.</param>
    static member Delete(path : string) = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        return! ofAsync <| config.FileStore.DeleteFile path
    }

    /// <summary>
    ///     Creates a new file in store with provided serializer function.
    /// </summary>
    /// <param name="path">Path to new cloud file.</param>
    /// <param name="serializer">Serializer function.</param>
    static member Create(path : string, serializer : Stream -> Async<unit>) : Local<CloudFile> = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        do! ofAsync <| async {
            use! stream = config.FileStore.BeginWrite path
            do! serializer stream
        }
        return new CloudFile(path)
    }

    /// <summary>
    ///     Reads file in store with provided deserializer function.
    /// </summary>
    /// <param name="path">Path to input file.</param>
    /// <param name="deserializer">Deserializer function.</param>
    static member Read<'T>(path : string, deserializer : Stream -> Async<'T>) : Local<'T> = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        return! ofAsync <| config.FileStore.Read(deserializer, path)
    }

    /// <summary>
    ///     Gets all files that exist in given container.
    /// </summary>
    /// <param name="dirPath">Path to directory.</param>
    static member Enumerate(dirPath : string) : Local<CloudFile []> = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        let! paths = ofAsync <| config.FileStore.EnumerateFiles(dirPath)
        return paths |> Array.map (fun path -> new CloudFile(path))
    }

    //
    //  Cloud file text utilities
    //

    /// <summary>
    ///     Writes a sequence of lines to a given CloudFile path.
    /// </summary>
    /// <param name="path">Path to new cloud file.</param>
    /// <param name="lines">Lines to be written.</param>
    /// <param name="encoding">Text encoding.</param>
    static member WriteAllLines(path : string, lines : seq<string>, ?encoding : Encoding) : Local<CloudFile> = local {
        let writer (stream : Stream) = async {
            use sw = 
                match encoding with
                | None -> new StreamWriter(stream)
                | Some e -> new StreamWriter(stream, e)

            do for line in lines do sw.WriteLine(line)
        }

        return! CloudFile.Create(path, writer)
    }

    /// <summary>
    ///     Reads a file as a sequence of lines.
    /// </summary>
    /// <param name="path">Path to input file.</param>
    /// <param name="encoding">Text encoding.</param>
    static member ReadLines(path : string, ?encoding : Encoding) : Local<seq<string>> = local {
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        let store = config.FileStore
        let mkEnumerator () =
            let stream = store.BeginRead path |> Async.RunSync
            let seq = TextReaders.ReadLines(stream, ?encoding = encoding)
            seq.GetEnumerator()

        return Seq.fromEnumerator mkEnumerator
    }

    /// <summary>
    ///     Reads a file as an array of lines.
    /// </summary>
    /// <param name="path">Path to input file.</param>
    /// <param name="encoding">Text encoding.</param>
    static member ReadAllLines(path : string, ?encoding : Encoding) : Local<string []> = local {
        let reader (stream : Stream) = async {
            let lines = TextReaders.ReadLines(stream, ?encoding = encoding)
            return Seq.toArray lines
        }

        return! CloudFile.Read(path, reader)
    }

    /// <summary>
    ///     Writes string contents to given CloudFile.
    /// </summary>
    /// <param name="path">Path to Cloud file.</param>
    /// <param name="text">Input text.</param>
    /// <param name="encoding">Output encoding.</param>
    static member WriteAllText(path : string, text : string, ?encoding : Encoding) : Local<CloudFile> = local {
        let writer (stream : Stream) = async {
            use sw = 
                match encoding with
                | None -> new StreamWriter(stream)
                | Some e -> new StreamWriter(stream, e)
            do! sw.WriteAsync text
        }

        return! CloudFile.Create(path, writer)
    }

    /// <summary>
    ///     Dump all file contents to a single string.
    /// </summary>
    /// <param name="path">Path to input file.</param>
    /// <param name="encoding">Text encoding.</param>
    static member ReadAllText(path : string, ?encoding : Encoding) = local {
        let reader (stream : Stream) = async {
            use sr = 
                match encoding with
                | None -> new StreamReader(stream)
                | Some e -> new StreamReader(stream, e)
            return sr.ReadToEnd()
        }
        return! CloudFile.Read(path, reader)
    }

    /// <summary>
    ///     Write buffer contents to CloudFile.
    /// </summary>
    /// <param name="path">Path to Cloud file.</param>
    /// <param name="buffer">Source buffer.</param>
    static member WriteAllBytes(path : string, buffer : byte []) : Local<CloudFile> = local {
        let writer (stream : Stream) = stream.AsyncWrite(buffer, 0, buffer.Length)
        return! CloudFile.Create(path, writer)
    }
        
    /// <summary>
    ///     Store all contents of given file to a new byte array.
    /// </summary>
    /// <param name="path">Path to input file.</param>
    static member ReadAllBytes(path : string) : Local<byte []> = local {
        let reader (stream : Stream) = async {
            use ms = new MemoryStream()
            do! stream.CopyToAsync ms
            return ms.ToArray()
        }

        return! CloudFile.Read(path, reader)
    }

    /// <summary>
    ///     Uploads a file from local disk to store.
    /// </summary>
    /// <param name="sourcePath">Path to file in local disk.</param>
    /// <param name="targetPath">Path to target file in cloud store.</param>
    /// <param name="overwrite">Enables overwriting of target file if it exists. Defaults to false.</param>
    static member Upload(sourcePath : string, targetPath : string, ?overwrite : bool) : Local<CloudFile> = local {
        let overwrite = defaultArg overwrite false
        let! config = Cloud.GetResource<CloudFileStoreConfiguration>()
        if not overwrite then
            let! exists = ofAsync <| config.FileStore.FileExists targetPath
            if exists then raise <| new IOException(sprintf "The file '%s' already exists." targetPath)

        use fs = File.OpenRead (Path.GetFullPath sourcePath)
        do! ofAsync <| config.FileStore.CopyOfStream(fs, targetPath)
        return new CloudFile(targetPath)
    }

    /// <summary>
    ///     Downloads a file from store to local disk.
    /// </summary>
    /// <param name="sourcePath">Source path to file in store.</param>
    /// <param name="targetPath">Path to target directory in local disk.</param>
    /// <param name="overwrite">Enables overwriting of target file if it exists. Defaults to false.</param>
    static member Download(sourcePath : string, targetPath : string, ?overwrite : bool) : Local<unit> = local {
        let overwrite = defaultArg overwrite false
        let targetPath = Path.GetFullPath targetPath
        let! config = Cloud.GetResource<CloudFileStoreConfiguration> ()
        if not overwrite && File.Exists targetPath then
            raise <| new IOException(sprintf "The file '%s' already exists." targetPath)

        use fs = File.OpenWrite targetPath
        do! ofAsync <| config.FileStore.CopyToStream(sourcePath, fs)
    }