// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// 2019/04/12 (Jerome Laban <jerome.laban@nventive.com>):
//	- Extracted from dotnet.exe
// 2024/12/05 (Jerome Laban <jerome@platform.uno>):
//	- Updated for nullability
//


namespace Uno.DevTools.Telemetry
{
    public interface ITelemetry
    {
        bool Enabled { get; }
        string? MachineId { get; }

        void Dispose();
        void Flush();
        Task FlushAsync(CancellationToken ct);
        void ThreadBlockingTrackEvent(string eventName, IDictionary<string, string> properties, IDictionary<string, double> measurements);
        void TrackEvent(string eventName, (string key, string value)[]? properties, (string key, double value)[]? measurements);
        void TrackEvent(string eventName, IDictionary<string, string>? properties, IDictionary<string, double>? measurements);
    }
}