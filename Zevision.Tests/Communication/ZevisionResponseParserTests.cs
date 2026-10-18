using System;
using Xunit;
using Zevision.Communication;

namespace Zevision.Tests
{
    public class ZevisionResponseParserTests
    {
        [Theory]
        [InlineData("UG1304:1", "UG1304")]
        [InlineData("QG01", "QG01")]
        [InlineData("UF0101:a:b", "UF0101")]
        [InlineData("UG1304:1;H01", "UG1304")]
        [InlineData(" QG01 ;QG1304", "QG01")]
        public void EchoOf_IsFirstCommandBeforeDataSeparator(string command, string echo)
        {
            Assert.Equal(echo, ZevisionResponseParser.EchoOf(command));
        }

        [Fact]
        public void Parse_SuccessWithData()
        {
            var response = Assert.Single(ZevisionResponseParser.Parse("QG01+:1.00"));

            Assert.Equal("QG01", response.Command);
            Assert.True(response.IsSuccess);
            Assert.Equal(ZevisionErrorCode.None, response.ErrorCode);
            Assert.Equal("1.00", response.Data);
        }

        [Fact]
        public void Parse_SuccessWithoutData()
        {
            var response = Assert.Single(ZevisionResponseParser.Parse("UG1304+"));

            Assert.Equal("UG1304", response.Command);
            Assert.True(response.IsSuccess);
            Assert.Null(response.Data);
        }

        [Theory]
        [InlineData("QG99-A", 'A', ZevisionErrorCode.IllegalCommand)]
        [InlineData("UG1304-B", 'B', ZevisionErrorCode.IllegalArgument)]
        [InlineData("H02-C", 'C', ZevisionErrorCode.ChecksumError)]
        [InlineData("QX-D", 'D', ZevisionErrorCode.IllegalFormat)]
        [InlineData("SC03-F", 'F', ZevisionErrorCode.StateError)]
        [InlineData("H01-L", 'L', ZevisionErrorCode.LengthError)]
        [InlineData("H01-T", 'T', ZevisionErrorCode.Timeout)]
        [InlineData("H01-Z", 'Z', ZevisionErrorCode.Unknown)]
        public void Parse_ErrorCodes(string payload, char raw, ZevisionErrorCode expected)
        {
            var response = Assert.Single(ZevisionResponseParser.Parse(payload));

            Assert.False(response.IsSuccess);
            Assert.Equal(raw, response.RawErrorCode);
            Assert.Equal(expected, response.ErrorCode);
        }

        [Fact]
        public void Parse_ErrorWithoutCommandEcho()
        {
            // 实机:校验和错误时设备不回显命令,只回 "-C"。
            var response = Assert.Single(ZevisionResponseParser.Parse("-C"));

            Assert.Equal(string.Empty, response.Command);
            Assert.False(response.IsSuccess);
            Assert.Equal(ZevisionErrorCode.ChecksumError, response.ErrorCode);
        }

        [Fact]
        public void Parse_ResponseWithFiller_FirstIsActualCommand()
        {
            var responses = ZevisionResponseParser.Parse("AL0702010600+;H01+:Zevision IMC300 1.8.11");

            Assert.Equal("AL0702010600", responses[0].Command);
            Assert.True(responses[0].IsSuccess);
            Assert.Equal("H01", responses[1].Command);
        }

        [Fact]
        public void Parse_ErrorWithoutCode()
        {
            var response = Assert.Single(ZevisionResponseParser.Parse("QG01-"));

            Assert.False(response.IsSuccess);
            Assert.Null(response.RawErrorCode);
            Assert.Equal(ZevisionErrorCode.Unknown, response.ErrorCode);
        }

        [Fact]
        public void Parse_MultipleResponses()
        {
            var responses = ZevisionResponseParser.Parse("SC03+:12.5;SC04+:30.1");

            Assert.Equal(2, responses.Count);
            Assert.Equal("SC03", responses[0].Command);
            Assert.Equal("12.5", responses[0].Data);
            Assert.Equal("SC04", responses[1].Command);
            Assert.Equal("30.1", responses[1].Data);
        }

        [Fact]
        public void Parse_DataContainingColon_KeepsRest()
        {
            var response = Assert.Single(ZevisionResponseParser.Parse("QM0101+:Name:with:colons"));

            Assert.Equal("Name:with:colons", response.Data);
        }

        [Fact]
        public void Parse_SemicolonInsideData_IsKeptInData()
        {
            var response = Assert.Single(ZevisionResponseParser.Parse("?+:H01 hello; H02 version;\r\nE echo"));

            Assert.Equal("?", response.Command);
            Assert.Equal("H01 hello; H02 version;\r\nE echo", response.Data);
        }

        [Fact]
        public void Parse_TrailingSemicolon_IsIgnored()
        {
            var response = Assert.Single(ZevisionResponseParser.Parse("H01+:abc;"));

            Assert.Equal("abc", response.Data);
        }

        [Fact]
        public void Parse_MissingStatus_Throws()
        {
            Assert.Throws<FormatException>(() => ZevisionResponseParser.Parse("H01:abc"));
        }

        [Fact]
        public void TryParse_Malformed_ReturnsFalse()
        {
            Assert.False(ZevisionResponseParser.TryParse("garbage", out var responses));
            Assert.Empty(responses);
        }
    }
}
