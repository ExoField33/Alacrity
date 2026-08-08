using System;
using System.Threading.Tasks;

namespace Alacrity.Core;

/// <summary>
/// Internal activation-local background-work boundary. It keeps lifecycle teardown independent
/// from the process-wide scheduler so disabling one plugin cannot pause unrelated plugins.
/// </summary>
internal interface IActivationBackgroundWork
{
    Task<bool> StopAndDrainBackgroundWorkAsync(TimeSpan timeout);
}

/// <summary>Implemented by host contexts that can coordinate their activation's background work.</summary>
internal interface IActivationBackgroundWorkContext
{
    Task<bool> StopAndDrainBackgroundWorkAsync(TimeSpan timeout);
}
