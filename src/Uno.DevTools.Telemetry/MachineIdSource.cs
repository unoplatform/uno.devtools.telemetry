// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using Uno.DevTools.Telemetry.Helpers;

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// Chooses the network address the desktop Machine ID is hashed from, and recognizes the ids that
    /// earlier versions of this package hashed from an address many machines share.
    /// </summary>
    /// <remarks>
    /// The Machine ID becomes the Application Insights user id, so an address that many machines share
    /// merges all of them into a single user. Earlier versions hashed the first interface that was up,
    /// which is the loopback adapter on iOS, Android, macOS and Linux, and often a VPN or VM adapter on
    /// Windows.
    /// </remarks>
    internal static class MachineIdSource
    {
        // Addresses that one product assigns identically on every machine it is installed on. A five-byte
        // entry covers every address that starts with it, because the product numbers its adapters in the
        // last byte.
        private static readonly byte[][] SharedAddressPrefixes =
        [
            [0x00, 0x50, 0x56, 0xC0, 0x00],       // VMware host-only and NAT adapters (vmnet1, vmnet8, ...)
            [0x0A, 0x00, 0x27, 0x00, 0x00],       // VirtualBox host-only adapters
            [0x00, 0x05, 0x9A, 0x3C, 0x7A, 0x00], // Cisco AnyConnect / Secure Client
            [0x00, 0x09, 0x0F, 0xFE, 0x00, 0x01], // FortiClient
            [0x02, 0x50, 0x41, 0x00, 0x00, 0x01], // GlobalProtect
        ];

        private static readonly Lazy<HashSet<string>> SharedMachineIds = new Lazy<HashSet<string>>(BuildSharedMachineIds);

        /// <summary>
        /// Returns the address of the first interface that is up and whose address identifies this
        /// machine alone, formatted the way the Machine ID has always been hashed from; or
        /// <see langword="null"/> when there is no such interface, in which case the caller uses a
        /// random id instead.
        /// </summary>
        internal static string? GetUniqueAddress(Func<IEnumerable<NetworkInterface>> getNetworkInterfaces)
        {
            IEnumerable<NetworkInterface> interfaces;
            try
            {
                interfaces = getNetworkInterfaces();
            }
            catch (Exception)
            {
                // Some platforms cannot enumerate interfaces at all.
                return null;
            }

            foreach (var nic in interfaces)
            {
                try
                {
                    if (nic.OperationalStatus == OperationalStatus.Up
                        && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && nic.GetPhysicalAddress() is { } address
                        && IsUniqueAddress(address.GetAddressBytes()))
                    {
                        return address.ToString();
                    }
                }
                catch (Exception)
                {
                    // One interface failing to report its state must not hide the ones after it.
                }
            }

            return null;
        }

        /// <summary>
        /// Whether a hardware address identifies one machine: a six-byte address assigned by the
        /// adapter's manufacturer, and not one that a product assigns identically everywhere.
        /// </summary>
        /// <remarks>
        /// The low bit of the first byte marks a group (multicast) address, which no adapter owns. The
        /// next bit marks a locally administered address, one assigned by software rather than the
        /// manufacturer: the placeholder iOS and Android report instead of the real address, container,
        /// VM and VPN adapters, and randomized Wi-Fi addresses. Some of those are the same on every
        /// machine and the rest can change, so none of them identifies a machine. Empty and all-zero
        /// addresses come from loopback adapters, and other lengths from tunnel pseudo-interfaces.
        /// </remarks>
        internal static bool IsUniqueAddress(byte[] address)
            => address.Length == 6
                && (address[0] & 0x03) == 0
                && address.Any(b => b != 0)
                && !SharedAddressPrefixes.Any(prefix => address.Take(prefix.Length).SequenceEqual(prefix));

        /// <summary>
        /// Whether a stored Machine ID was hashed from an address many machines share, and so has to be
        /// replaced rather than reused.
        /// </summary>
        internal static bool IsSharedMachineId(string machineId)
            => SharedMachineIds.Value.Contains(machineId);

        private static HashSet<string> BuildSharedMachineIds()
        {
            // A loopback adapter reports no address on Windows, macOS, iOS and Android, and an all-zero
            // one on Linux. Hashing them the way GetUniqueAddress formats an address reproduces the ids
            // that earlier versions stored.
            var ids = new HashSet<string>(StringComparer.Ordinal)
            {
                HashBuilder.Build(""),
                HashBuilder.Build("000000000000"),
            };

            foreach (var prefix in SharedAddressPrefixes)
            {
                foreach (var address in ExpandPrefix(prefix))
                {
                    ids.Add(HashBuilder.Build(new PhysicalAddress(address).ToString()));
                }
            }

            return ids;
        }

        private static IEnumerable<byte[]> ExpandPrefix(byte[] prefix)
        {
            if (prefix.Length == 6)
            {
                yield return prefix;
                yield break;
            }

            for (var last = 0; last <= byte.MaxValue; last++)
            {
                yield return [.. prefix, (byte)last];
            }
        }
    }
}
