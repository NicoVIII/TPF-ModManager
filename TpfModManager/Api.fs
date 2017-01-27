﻿namespace TpfModManager

[<AllowNullLiteral>]
type Settings(``internal``: SettingsModule.T) =
    let mutable ``internal`` = ``internal``

    member x.Save () = SettingsModule.saveSettings ``internal``
    member x.TpfModPath
        with get() =
            ``internal``.tpfModPath
        and set(value) = 
            ``internal`` <- {``internal`` with tpfModPath = value}
    new () = Settings({tpfModPath = ""})

module private SettingsApi =
    let convert settings =
        match settings with
        | None -> null
        | Some settings -> new Settings(settings)

type Author(``internal`` :Types.Author) =
    member val Name =
        ``internal``.name
        with get, set
    member val TpfNetId =
        ``internal``.tpfNetId
        with get, set

module private AuthorApi =
    let deconvert (author :Author) =
        {Types.Author.name = author.Name; Types.Author.tpfNetId = author.TpfNetId}

[<AllowNullLiteralAttribute>]
type Version(``internal``: ModList.Version) =
    member val Major =
        ``internal``.major
        with get, set
    member val Minor =
        ``internal``.minor
        with get, set

type Mod(``internal``: ModList.Mod) =
    member val Image =
        match ``internal``.image with
        | None -> ""
        | Some i -> i
        with get, set
    member val Folder =
        ``internal``.folder
        with get, set
    member val Name =
        ``internal``.name
        with get, set
    member val Authors =
        ``internal``.authors
        |> List.map (fun author -> new Author(author))
        |> List.toArray
        with get, set
    member val Version =
        new Version(``internal``.version)
        with get, set
    member val TpfNetId =
        ``internal``.tpfNetId
        with get, set
    member val RemoteVersion =
        match ``internal``.remoteVersion with
        | None -> null
        | Some remoteVersion -> new Version(remoteVersion)
        with get, set

module private ModApi =
    let convert ``mod`` =
        new Mod(``mod``)
    let deconvert (``mod`` :Mod) =
        {
            ModList.Mod.name = ``mod``.Name;
            ModList.Mod.authors =
                ``mod``.Authors
                |> Array.toList
                |> List.map AuthorApi.deconvert;
            ModList.Mod.folder = ``mod``.Folder;
            ModList.Mod.image =
                match ``mod``.Image with
                | "" -> None
                | image -> Some image
            ModList.Mod.version = {major = ``mod``.Version.Major; minor = ``mod``.Version.Minor}
            ModList.Mod.tpfNetId = ``mod``.TpfNetId
            ModList.Mod.remoteVersion =
                match ``mod``.RemoteVersion with
                | null -> None
                | version -> 
                    Some {major = version.Major; minor = version.Minor}
        }

type InstallationResult =
    | Success = 0
    | AlreadyInstalled = 1
    | ModInvalid = 2
    | NotAnArchive = 3
    | NotSupported = 4

type ModManager() =
    let csv = TpfNet.getCSV()

    member val Settings =
        SettingsModule.loadSettings()
        |> SettingsApi.convert
        with get, set
    member val ModList =
        ModList.loadModList()
        |> List.map ModApi.convert
        |> List.toArray
        with get, set

    member x.LookUpRemoteVersions() =
        match csv with
        | None -> ()
        | Some csv ->
            x.ModList <-
                x.ModList
                |> Array.toList
                |> List.map ModApi.deconvert
                |> List.map (function ``mod`` -> {``mod`` with remoteVersion = TpfNet.lookUpRemoteVersion csv ``mod``})
                |> List.map ModApi.convert
                |> List.toArray

    member x.Check() =
        x.ModList <-
            ModList.createModListFromPath x.Settings.TpfModPath
            |> List.map ModApi.convert
            |> List.toArray
    member x.Install(modArchivePath) =
        let modList =
            x.ModList
            |> Array.toList
            |> List.map ModApi.deconvert
        match Installation.install modList x.Settings.TpfModPath modArchivePath with
        | Ok result ->
            x.ModList <-
                result
                |> List.map ModApi.convert
                |> List.toArray
            InstallationResult.Success
        | Error error ->
            match error with
            | Installation.AlreadyInstalled ->
                InstallationResult.AlreadyInstalled
            | Installation.ModInvalid ->
                InstallationResult.ModInvalid
            | Installation.NotAnArchive ->
                InstallationResult.NotAnArchive
            | Installation.ModListError
            | Installation.NoFolderIncluded ->
                InstallationResult.NotSupported
            | Installation.ExtractionFailed ->
                printfn "%A" error
                failwith "error"