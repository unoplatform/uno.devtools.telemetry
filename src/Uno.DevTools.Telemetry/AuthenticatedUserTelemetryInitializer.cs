// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ApplicationInsights.Extensibility;

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// Stamps the ambient authenticated user id (<see cref="TelemetryUserContext.AuthenticatedUserId"/>)
    /// onto each telemetry item at track time. Emitted as the <c>ai.user.authUserId</c> context tag,
    /// surfaced as <c>user_AuthenticatedId</c> in Application Insights.
    /// </summary>
    internal sealed class AuthenticatedUserTelemetryInitializer : ITelemetryInitializer
    {
        // Initializers run synchronously on the tracking thread against the item's own context,
        // so no shared client context is mutated (the track task chain is not fully serialized).
        public void Initialize(Microsoft.ApplicationInsights.Channel.ITelemetry telemetry)
        {
            var authenticatedUserId = TelemetryUserContext.AuthenticatedUserId;
            if (authenticatedUserId is not null)
            {
                telemetry.Context.User.AuthenticatedUserId = authenticatedUserId;
            }
        }
    }
}
