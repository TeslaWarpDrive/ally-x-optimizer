using AllyTuner.Core.Asus;

namespace AllyTuner.Core.Tests.Asus;

public class AsusAcpiProtocolTests
{
    [Fact]
    public void Constants_match_the_cited_sources()
    {
        Assert.Equal(@"\\.\ATKACPI", AsusAcpiProtocol.DevicePath);
        Assert.Equal(0x0022240Cu, AsusAcpiProtocol.IoctlCode);
        Assert.Equal(0x53545344u, AsusAcpiProtocol.MethodDeviceStatus);
        Assert.Equal(0x00010000u, AsusAcpiProtocol.PresenceBit);
        Assert.Equal(0xFFFFFFFEu, AsusAcpiProtocol.UnsupportedResult);
    }

    [Fact]
    public void Device_status_method_is_DSTS_in_ascii()
    {
        var bytes = BitConverter.GetBytes(AsusAcpiProtocol.MethodDeviceStatus);
        Assert.Equal("DSTS", System.Text.Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void Ioctl_needs_no_write_access()
    {
        // CTL_CODE layout: bits 14-15 are the required access. 0 = FILE_ANY_ACCESS.
        var access = (AsusAcpiProtocol.IoctlCode >> 14) & 0x3;
        var method = AsusAcpiProtocol.IoctlCode & 0x3;
        Assert.Equal(0u, access);
        Assert.Equal(0u, method); // METHOD_BUFFERED
    }

    [Fact]
    public void Standard_request_matches_GHelper_DeviceGet_layout()
    {
        var request = AsusAcpiProtocol.BuildDeviceStatusRequest(0x00120075, AsusRequestShape.Standard);

        Assert.Equal(
            new byte[] { 0x44, 0x53, 0x54, 0x53, 0x08, 0, 0, 0, 0x75, 0x00, 0x12, 0x00, 0, 0, 0, 0 },
            request);
    }

    [Fact]
    public void Standard_request_carries_the_argument()
    {
        var request = AsusAcpiProtocol.BuildDeviceStatusRequest(0x00110024, AsusRequestShape.Standard, argument: 2);

        Assert.Equal(16, request.Length);
        Assert.Equal(2u, BitConverter.ToUInt32(request, 12));
    }

    [Fact]
    public void Extended_request_matches_GHelper_DeviceGetLarge_layout()
    {
        var request = AsusAcpiProtocol.BuildDeviceStatusRequest(0x000600C4, AsusRequestShape.Extended);

        Assert.Equal(
            new byte[] { 0x44, 0x53, 0x54, 0x53, 0x0C, 0, 0, 0, 0xC4, 0x00, 0x06, 0x00, 0, 0, 0, 0, 0, 0, 0, 0 },
            request);
    }

    [Fact]
    public void Extended_request_rejects_an_argument()
    {
        Assert.Throws<ArgumentException>(() =>
            AsusAcpiProtocol.BuildDeviceStatusRequest(0x000600C4, AsusRequestShape.Extended, argument: 1));
    }

    [Theory]
    [InlineData(AsusRequestShape.Standard)]
    [InlineData(AsusRequestShape.Extended)]
    public void Every_request_uses_the_read_method(AsusRequestShape shape)
    {
        foreach (var endpoint in AsusEndpointCatalog.All)
        {
            var request = AsusAcpiProtocol.BuildDeviceStatusRequest(endpoint.Id, shape);
            Assert.Equal(AsusAcpiProtocol.MethodDeviceStatus, BitConverter.ToUInt32(request, 0));
        }
    }

    [Fact]
    public void Read_result_is_little_endian_and_needs_four_bytes()
    {
        Assert.Equal(0x00010007u, AsusAcpiProtocol.ReadResult(Rc73xaPublicScan.Scalar(Rc73xaPublicScan.PerformanceModeRaw)));
        Assert.Null(AsusAcpiProtocol.ReadResult(new byte[3]));
    }

    [Fact]
    public void Response_buffer_is_big_enough_for_the_RC73XA_VRAM_list()
    {
        // 18 options * 2 bytes + 6 header bytes = 42; G-Helper needed more than 40.
        Assert.True(AsusAcpiProtocol.ResponseSize >= 42);
    }
}
