﻿namespace TPFModManager

open FSharp.Data
open ModInfo
open RobHelper
open SharpCompress.Archives
open SharpCompress.Readers
open System
open System.IO
open System.IO.Compression
open Types

type ModUrl = ModUrl of String

// Internal logic to provide API functionality
module private Internal =
    // List
    let list () =
        loadModInfo()
        |> List.sortBy (fun {name=name} -> name)
    
    // Download
    let directoryFromFile (path :string) =
        path.Split [| '/' |]
        |> Array.toList
        |> List.rev
        |> List.tail
        |> List.rev
        |> List.fold (fun path folder -> path+folder+"/") ""

    let invalidFilePathChars = "\\"::"/"::["|"]

    let saveBytes {name = name; version = version; url = url; bytes = bytes; title = title} =
        // Replace invalid characters
        let name' = invalidFilePathChars |> List.fold (fun (name :string) c -> name.Replace(c, "_")) name
        let extension::_ = title.Split '.' |> List.ofArray |> List.rev
        let path = ("tmp/"+name'+"-"+version+"."+extension)
        directoryFromFile path
        |> Directory.CreateDirectory |> ignore
        File.WriteAllBytes(path, bytes)
        {name = name; version = version; url = url; archivePath = path}

    let download (downloadStartedEvent :Event<_>) (downloadEndedEvent :Event<_>) =
        TransportFeverNet.getInfo
        >> map (tee (ApiHelper.convertModInfo >> downloadStartedEvent.Trigger))
        >> bind TransportFeverNet.getFileBytes
        >> map saveBytes
        >> map (tee (ApiHelper.convertModDownloadedInfo >> downloadEndedEvent.Trigger))

    // Installation
    let checkModInstalled url =
        let (Url urlString) = url
        let mods = loadModInfo()
        let fold state {folder = _; url = url} = state || url = urlString
        let installed = List.fold fold false mods
        match installed with
        | true -> Error [AlreadyInstalled]
        | false -> Ok url
    
    let prepareExtract (settings :Settings) modInfoDownloaded =
        let {name = name; version = version; url = url; archivePath = archivePath} = modInfoDownloaded
        use archive = ArchiveFactory.Open archivePath
        let fold list (entry :IArchiveEntry) =
            let name = entry.Key.TrimEnd '/'
            let name' = (name.Split [| '\\'; '/'|]).[0]
            if List.forall (fun el -> not (el = name')) list then
                name'::list
            else
                list
        let entries = archive.Entries |> Seq.toList |> List.fold fold []
        archive.Dispose ()
        match entries with
        | [folder] -> 
            Ok {modDownloadedInfo=modInfoDownloaded; extractPath = settings.tpfModPath; folder = folder}
        | list ->
            if list |> List.exists (fun el -> el = "mod.lua") then
                Ok {modDownloadedInfo=modInfoDownloaded; extractPath = settings.tpfModPath+"/"+name; folder = name}
            else 
                Error [ModInvalid]

    let performExtract (settings :Settings) {modDownloadedInfo = {name = name; version = version; url = url; archivePath = archivePath}; folder = folder; extractPath = extractPath} =
        try
            use archive = ArchiveFactory.Open archivePath
            use reader = archive.ExtractAllEntries()
            let eo = new ExtractionOptions()
            eo.ExtractFullPath <- true
            reader.WriteAllToDirectory (extractPath,eo)
            reader.Dispose()
            archive.Dispose()
            if settings.deleteArchives then File.Delete(archivePath)
            Ok {name = name; websiteVersion = version; url = url; folder = folder}
        with
        | :? System.IO.IOException -> Error [ExtractionFailed]

    let extract (settings :Settings) (extractStartedEvent :Event<_>) (extractEndedEvent :Event<_>) =
        prepareExtract settings
        >> map (tee (ApiHelper.convertExtractInfo >> extractStartedEvent.Trigger))
        >> bind (performExtract settings)
        >> map (tee (ApiHelper.convertMod >> extractEndedEvent.Trigger))

    (*let prepareExtractZip (settings :Settings) modInfoDownloaded =
        let {name = name; version = version; url = url; archivePath = zipPath} = modInfoDownloaded
        use file = ZipFile.Open(zipPath, ZipArchiveMode.Read)
        let fold list (entry :ZipArchiveEntry) =
            let name = entry.FullName.TrimEnd '/'
            let name' = (name.Split '/').[0]
            if List.forall (fun el -> not (el = name')) list then
                name'::list
            else
                list
        let entries = file.Entries |> Seq.toList |> List.fold fold []
        file.Dispose ()
        match entries with
        | [folder] -> 
            Ok {modDownloadedInfo=modInfoDownloaded; extractPath = settings.tpfModPath; folder = folder}
        | list ->
            if list |> List.exists (fun el -> el = "mod.lua") then
                Ok {modDownloadedInfo=modInfoDownloaded; extractPath = settings.tpfModPath+"/"+name; folder = name}
            else 
                Error [ModInvalid]

    let performExtractZip (settings :Settings) {modDownloadedInfo = {name = name; version = version; url = url; archivePath = zipPath}; folder = folder; extractPath = extractPath} =
        try
            ZipFile.ExtractToDirectory(zipPath, extractPath) |> ignore
            if settings.deleteArchives then File.Delete(zipPath)
            Ok {name = name; websiteVersion = version; url = url; folder = folder}
        with
        | :? System.IO.IOException -> Error [ExtractionFailed]

    let extractZip (settings :Settings) (extractStartedEvent :Event<_>) (extractEndedEvent :Event<_>) =
        prepareExtractZip settings
        >> map (tee (ApiHelper.convertExtractInfo >> extractStartedEvent.Trigger))
        >> bind (performExtractZip settings)
        >> map (tee (ApiHelper.convertMod >> extractEndedEvent.Trigger))*)

    (*let extract (settings :Settings) (extractStartedEvent :Event<_>) (extractEndedEvent :Event<_>) (modInfoDownloaded :ModDownloadedInfo) =
        if modInfoDownloaded.archivePath.EndsWith ".zip" then
            extractZip settings extractStartedEvent extractEndedEvent modInfoDownloaded
        else if modInfoDownloaded.archivePath.EndsWith ".rar" then
            extractRar settings extractStartedEvent extractEndedEvent modInfoDownloaded
        else
            Error [NotSupportedFormat "?"]*)

    let install (settings :Settings) (downloadStartedEvent :Event<_>) (downloadEndedEvent :Event<_>) (extractStartedEvent :Event<_>) (extractEndedEvent :Event<_>) =
        checkModInstalled
        >> bind (download downloadStartedEvent downloadEndedEvent)
        >> bind (extract settings extractStartedEvent extractEndedEvent)

    let installSingle (settings :Settings) (downloadStartedEvent :Event<_>) (downloadEndedEvent :Event<_>) (extractStartedEvent :Event<_>) (extractEndedEvent :Event<_>) =
        install settings downloadStartedEvent downloadEndedEvent extractStartedEvent extractEndedEvent 
        >> map addMod

    let update () =
        let fold list (_mod :Mod) =
            match TransportFeverNet.getVersion (Url _mod.url) with
            | Ok newVersion ->
                if not (newVersion = _mod.websiteVersion) then
                    (_mod.name, _mod.websiteVersion, newVersion)::list
                else
                    list
            // HACK add error handling
            | Error _ -> list

        loadModInfo()
        |> List.fold fold []

    let upgrade (settings :Settings) (extractStartedEvent :Event<_>) (extractEndedEvent :Event<_>) (downloadStartedEvent :Event<_>) (downloadEndedEvent :Event<_>) (_mod :Mod) =
        match TransportFeverNet.getVersion (Url _mod.url) with
        | Ok newVersion ->
            if not (newVersion = _mod.websiteVersion) then
                Directory.Delete(settings.tpfModPath+"/"+_mod.folder, true)
                removeModInfo _mod
                installSingle settings extractStartedEvent extractEndedEvent downloadStartedEvent downloadEndedEvent (Url _mod.url)
            else
                Ok ()
        // HACK add error handling
        | Error _ -> Error []