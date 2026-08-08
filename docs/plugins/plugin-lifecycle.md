# Plugin Lifecycle

Packages progress through discovery, validation, loading, fault/restart-required handling, and uninstall. Activations independently progress through initialization, enable, disable, and shutdown. Every activation receives a fresh `IPluginContext` and resource scope. Disabling releases resources in reverse registration order; old services reject use and cannot register into a later activation.

Dependencies enable in dependency-first order and cleanup rolls back in reverse order. Sync and async callbacks share the same controller; async work has cancellation and bounded shutdown handling.
