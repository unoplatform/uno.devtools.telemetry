using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using Uno.DevTools.Telemetry.Helpers;

namespace Uno.DevTools.Telemetry.Tests
{
    [TestClass]
    public class MachineIdSourceTests
    {
        private const string UniqueAddress = "3C22FB0A1B2C";
        private const string OtherUniqueAddress = "001A2B3C4D5E";

        private readonly string _storageDirectory = Path.Join(Path.GetTempPath(), $"telemetry_test_{Guid.NewGuid():N}");

        private string MachineHashPath => Path.Join(_storageDirectory, ".machinehash");

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_storageDirectory))
            {
                Directory.Delete(_storageDirectory, recursive: true);
            }
        }

        [TestMethod]
        [DataRow(UniqueAddress)]
        [DataRow(OtherUniqueAddress)]
        [DataRow("005056AB1234", DisplayName = "VMware virtual machine adapter, outside the host adapter block")]
        public void Given_ManufacturerAssignedAddress_When_Checked_Then_IsUnique(string address)
        {
            MachineIdSource.IsUniqueAddress(Convert.FromHexString(address)).Should().BeTrue();
        }

        [TestMethod]
        [DataRow("", DisplayName = "No address: loopback on Windows, macOS, iOS and Android")]
        [DataRow("000000000000", DisplayName = "All zero: loopback on Linux")]
        [DataRow("020000000000", DisplayName = "Placeholder reported by iOS and Android")]
        [DataRow("0242AC110002", DisplayName = "Docker container adapter")]
        [DataRow("3E22FB0A1B2C", DisplayName = "Randomized Wi-Fi address")]
        [DataRow("01005E000001", DisplayName = "Multicast address")]
        [DataRow("FFFFFFFFFFFF", DisplayName = "Broadcast address")]
        [DataRow("00000000000000E0", DisplayName = "Tunnel pseudo-interface")]
        [DataRow("005056C00001", DisplayName = "VMware vmnet1")]
        [DataRow("005056C00008", DisplayName = "VMware vmnet8")]
        [DataRow("0A0027000004", DisplayName = "VirtualBox host-only adapter")]
        [DataRow("00059A3C7A00", DisplayName = "Cisco AnyConnect")]
        [DataRow("00090FFE0001", DisplayName = "FortiClient")]
        [DataRow("025041000001", DisplayName = "GlobalProtect")]
        [DataRow("ACDE48001122", DisplayName = "Apple T2 bridge on Intel Macs")]
        public void Given_AddressNotUniqueToAMachine_When_Checked_Then_IsRejected(string address)
        {
            MachineIdSource.IsUniqueAddress(Convert.FromHexString(address)).Should().BeFalse();
        }

        [TestMethod]
        public void Given_LoopbackListedFirst_When_SelectingAnAddress_Then_TheNextUniqueAdapterIsUsed()
        {
            // Arrange: the order macOS, iOS, Android and Linux list their interfaces in.
            var interfaces = new NetworkInterface[] { Loopback(""), Adapter(UniqueAddress) };

            // Act
            var address = MachineIdSource.GetUniqueAddress(() => interfaces);

            // Assert
            address.Should().Be(UniqueAddress);
        }

        [TestMethod]
        public void Given_FirstInterfaceUpIsUnique_When_SelectingAnAddress_Then_ItIsUsed()
        {
            // The interface earlier versions picked is kept whenever it was already a good one, so a
            // machine that loses its stored id derives the same one again.
            var interfaces = new NetworkInterface[] { Adapter(UniqueAddress), Adapter(OtherUniqueAddress) };

            MachineIdSource.GetUniqueAddress(() => interfaces).Should().Be(UniqueAddress);
        }

        [TestMethod]
        public void Given_UniqueAddressOnAnInterfaceThatIsDown_When_SelectingAnAddress_Then_ItIsSkipped()
        {
            var interfaces = new NetworkInterface[] { Adapter(UniqueAddress, OperationalStatus.Down), Adapter(OtherUniqueAddress) };

            MachineIdSource.GetUniqueAddress(() => interfaces).Should().Be(OtherUniqueAddress);
        }

        [TestMethod]
        public void Given_LoopbackTypeWithAnAddress_When_SelectingAnAddress_Then_ItIsSkipped()
        {
            var interfaces = new NetworkInterface[] { Loopback(UniqueAddress), Adapter(OtherUniqueAddress) };

            MachineIdSource.GetUniqueAddress(() => interfaces).Should().Be(OtherUniqueAddress);
        }

        [TestMethod]
        public void Given_OnlyLoopbackAndSharedAddresses_When_SelectingAnAddress_Then_NoneIsReturned()
        {
            // Arrange: what iOS and Android expose, plus the adapters a VPN or VM product adds.
            var interfaces = new NetworkInterface[]
            {
                Loopback(""),
                Adapter("020000000000"),
                Adapter("005056C00001"),
                Adapter("00059A3C7A00"),
                Adapter(""),
            };

            // Act
            var address = MachineIdSource.GetUniqueAddress(() => interfaces);

            // Assert
            address.Should().BeNull();
        }

        [TestMethod]
        public void Given_AnInterfaceThatThrows_When_SelectingAnAddress_Then_TheNextOneIsUsed()
        {
            var interfaces = new NetworkInterface[] { new ThrowingInterface(), Adapter(UniqueAddress) };

            MachineIdSource.GetUniqueAddress(() => interfaces).Should().Be(UniqueAddress);
        }

        [TestMethod]
        public void Given_EnumerationThrows_When_SelectingAnAddress_Then_NoneIsReturned()
        {
            MachineIdSource.GetUniqueAddress(() => throw new NetworkInformationException()).Should().BeNull();
        }

        [TestMethod]
        [DataRow("e3b0c44298fc1c149afbf4c8996fb924", DisplayName = "No address: loopback on Windows, macOS, iOS and Android")]
        [DataRow("f7b11509f4d675c3c44f0dd37ca830bb", DisplayName = "000000000000: loopback on Linux")]
        [DataRow("8e58e26833fc7e5afbd793d982bd47ca", DisplayName = "005056C00001: VMware vmnet1")]
        [DataRow("7cff89750d238774d5e0dc68ce3f883b", DisplayName = "00059A3C7A00: Cisco AnyConnect")]
        [DataRow("5cb1a4bd4d6c285bf7e36c9bb18d95e7", DisplayName = "025041000001: GlobalProtect")]
        [DataRow("7fea0dbc3b921d3455e683dd69b461c9", DisplayName = "0A002700000A: VirtualBox host-only adapter")]
        [DataRow("f33b49c114f516088a96c26f81a959fe", DisplayName = "00090FFE0001: FortiClient")]
        [DataRow("28ca43a1725b799384358ad467cd510f", DisplayName = "ACDE48001122: Apple T2 bridge")]
        public void Given_IdStoredForASharedAddress_When_Checked_Then_IsShared(string machineId)
        {
            // These are the ids earlier versions stored for those addresses. The literals are pinned on
            // purpose, so a change to how addresses are formatted or hashed cannot pass silently.
            MachineIdSource.IsSharedMachineId(machineId).Should().BeTrue();
        }

        [TestMethod]
        public void Given_IdOfAUniqueAddressOrARandomId_When_Checked_Then_IsNotShared()
        {
            MachineIdSource.IsSharedMachineId(HashBuilder.Build(UniqueAddress)).Should().BeFalse();
            MachineIdSource.IsSharedMachineId(HashBuilder.Build(Guid.NewGuid().ToString())).Should().BeFalse();
            MachineIdSource.IsSharedMachineId(Guid.NewGuid().ToString()).Should().BeFalse();
        }

        [TestMethod]
        public void Given_NoUniqueAddress_When_MachineIdIsRead_Then_ARandomIdIsStoredAndReused()
        {
            // Arrange
            var interfaces = new NetworkInterface[] { Loopback(""), Adapter("020000000000") };

            // Act
            var first = GetMachineId(() => interfaces);
            var second = GetMachineId(() => interfaces);

            // Assert
            first.Should().HaveLength(32);
            MachineIdSource.IsSharedMachineId(first).Should().BeFalse();
            File.ReadAllText(MachineHashPath).Should().Be(first);
            second.Should().Be(first);
        }

        [TestMethod]
        public void Given_StoredIdForASharedAddress_When_MachineIdIsRead_Then_ItIsReplaced()
        {
            // Arrange: the id every macOS machine stored before the fix.
            Directory.CreateDirectory(_storageDirectory);
            File.WriteAllText(MachineHashPath, "e3b0c44298fc1c149afbf4c8996fb924");
            var interfaces = new NetworkInterface[] { Loopback(""), Adapter(UniqueAddress) };

            // Act
            var machineId = GetMachineId(() => interfaces);

            // Assert
            machineId.Should().Be(HashBuilder.Build(UniqueAddress));
            File.ReadAllText(MachineHashPath).Should().Be(machineId);
        }

        [TestMethod]
        public void Given_StoredIdOfItsOwn_When_MachineIdIsRead_Then_ItIsKept()
        {
            // Arrange: a good stored id wins even when the interfaces would now derive another one.
            Directory.CreateDirectory(_storageDirectory);
            var stored = HashBuilder.Build(OtherUniqueAddress);
            File.WriteAllText(MachineHashPath, stored);
            var interfaces = new NetworkInterface[] { Adapter(UniqueAddress) };

            // Act
            var machineId = GetMachineId(() => interfaces);

            // Assert
            machineId.Should().Be(stored);
        }

        [TestMethod]
        public void Given_EnumerationThrows_When_MachineIdIsRead_Then_TheRandomIdIsStillStored()
        {
            // Act
            var first = GetMachineId(() => throw new NetworkInformationException());
            var second = GetMachineId(() => throw new NetworkInformationException());

            // Assert
            first.Should().HaveLength(32);
            second.Should().Be(first);
        }

        private string GetMachineId(Func<IEnumerable<NetworkInterface>> getNetworkInterfaces)
            => new TelemetryCommonProperties(
                    _storageDirectory,
                    typeof(MachineIdSourceTests).Assembly,
                    "Test",
                    getNetworkInterfaces: getNetworkInterfaces)
                .GetTelemetryCommonProperties()[TelemetryCommonProperties.MachineId];

        private static FakeInterface Adapter(string address, OperationalStatus status = OperationalStatus.Up)
            => new FakeInterface(address, status, NetworkInterfaceType.Ethernet);

        private static FakeInterface Loopback(string address)
            => new FakeInterface(address, OperationalStatus.Up, NetworkInterfaceType.Loopback);

        private sealed class FakeInterface : NetworkInterface
        {
            private readonly byte[] _address;

            public FakeInterface(string address, OperationalStatus status, NetworkInterfaceType type)
            {
                _address = Convert.FromHexString(address);
                OperationalStatus = status;
                NetworkInterfaceType = type;
            }

            public override OperationalStatus OperationalStatus { get; }

            public override NetworkInterfaceType NetworkInterfaceType { get; }

            public override PhysicalAddress GetPhysicalAddress() => new PhysicalAddress(_address);
        }

        private sealed class ThrowingInterface : NetworkInterface
        {
            public override OperationalStatus OperationalStatus => throw new NetworkInformationException();
        }
    }
}
