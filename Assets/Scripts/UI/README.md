# GuildIdle UI architecture

`GuildIdle.UI.Core` owns only top-level UI navigation and lifecycle. It contains no gameplay-specific API and does not read or mutate `PlayerState`.

## Ownership and navigation

```text
UIRoot
  owns
UIService
  manages
UIScreen / UIWindow
```

`UIRoot` receives its four layer containers and a standalone `UIPrefabCatalog` through serialized references. It creates one `UIService` and disposes it when the root is destroyed. Consumers receive an explicit reference to the root or service; there is no UI singleton, Service Locator, or runtime lookup.

The global catalog registers only top-level `UIScreen` and `UIWindow` prefabs. A popup is a `UIWindow` registered in `UILayer.Popup`. `UIPanel`, cards, reusable components, and overlays are not opened through `UIService`.

## Presentation boundary

```text
Runtime / PlayerState snapshots / config repositories
    -> Presenter
    -> immutable or read-only UI State
    -> View

View user intent
    -> Presenter / Application API
    -> Runtime
```

Views render prepared state. They do not calculate requirements, rewards, progression, capacity, combat results, or perform gameplay mutations. Presenters are introduced only for features that need them; simple reusable components do not require a presenter/state pair.

Feature state may be specialized by presentation variant. A feature may define `IActivityCardState` with separate `RepeatableWorkCardState`, `CombatActivityCardState`, and `ConstructionActivityCardState` implementations. It does not need one DTO with many nullable fields.

## Feature-level prefab variants

Features may own catalogs, factories, or prefab registries for nested visual variants:

```text
UI/
  Heroes/
    HeroCardFactory
    HeroCardCatalog
    WorkingHeroCardView
    CombatHeroCardView
    ResultPendingHeroCardView

  Activities/
    ActivityCardFactory
    ActivityCardCatalog
    RepeatableWorkCardView
    CombatActivityCardView
    ExplorationActivityCardView
    ConstructionActivityCardView
```

These are feature extension points, not global navigation entries. A feature-level factory selects a visual prefab from presentation state:

```text
Runtime -> Presenter -> CombatActivityCardState -> ActivityCardFactory -> CombatActivityCard.prefab
```

Gameplay/runtime never selects a prefab and never references a concrete UI view. In particular, `ActivityRuntimeService -> CombatActivityCardView` and `Runtime -> prefab` dependencies are forbidden.

## Lifecycle

Top-level views follow an explicit lifecycle:

```text
Apply args -> Bind -> Show -> Hide -> Unbind -> Destroy
```

`Show` and `Hide` are idempotent. Bind creates a fresh cleanup scope; subscriptions are registered during bind and released during unbind. Rebind first performs `Hide/Unbind`, then applies new arguments and performs `Bind/Show` on the same instance. `Hide` leaves no active lifecycle subscriptions, and destroy always performs final cleanup.

Feature views must not create lifecycle subscriptions in `OnEnable`. They should create them in `OnBind` and register their matching unsubscribe actions with `RegisterCleanup`, or release them explicitly in `OnUnbind`.

Closed screens and windows are not cached. `UIService` owns only the current screen and currently open windows.
