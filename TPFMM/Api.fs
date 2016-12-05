﻿namespace TPFModManager.Api

open TPFModManager
open TPFModManager.ApiHelper
open TPFModManager.ModInfo
open TPFModManager.Types

// API
type TPFMM(settings :Settings) =
    // Events
    let downloadStartedEvent = new Event<_>()
    let downloadEndedEvent = new Event<_>()
    let extractStartedEvent = new Event<_>()
    let extractEndedEvent = new Event<_>()

    // Property
    member this.Settings = settings

    // Methods
    member this.List =
        Internal.list ()
        |> List.map convertMod
        |> Array.ofList
    member this.Install url = Internal.installSingle this.Settings extractStartedEvent extractEndedEvent downloadStartedEvent downloadEndedEvent (Url url)
    member this.Update =
        Internal.update ()
        |> List.map (fun (name, oldVersion, newVersion) -> [| name ; oldVersion ; newVersion |])
        |> Array.ofList
    //member this.UpgradeAll = Internal.upgradeAll this.Settings upgradeProcessEvent installProcessEvent

    // Callbacks
    member this.RegisterDownloadStartedListener handler = Event.add handler downloadStartedEvent.Publish
    member this.RegisterDownloadEndedListener handler = Event.add handler downloadEndedEvent.Publish
    member this.RegisterExtractStartedListener handler = Event.add handler extractStartedEvent.Publish
    member this.RegisterExtractEndedListener handler = Event.add handler extractEndedEvent.Publish

    // Static methods
    static member loadSettings =
        match Internal.tryLoadSettings () with
        | Some settings -> new Settings(settings.TpfModPath, settings.DeleteZips)
        | None -> null