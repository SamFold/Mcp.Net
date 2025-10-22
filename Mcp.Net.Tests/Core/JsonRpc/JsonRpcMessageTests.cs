using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Mcp.Net.Core.JsonRpc;
using Xunit;

namespace Mcp.Net.Tests.Core.JsonRpc;

public class JsonRpcMessageTests
{
    [Fact]
    public void JsonRpcRequestMessage_Should_Serialize_Correctly()
    {
        // Arrange
        var testParams = new { param1 = "value1", param2 = 42 };
        var requestMessage = new JsonRpcRequestMessage("2.0", "123", "test_method", testParams);

        // Act
        var json = JsonSerializer.Serialize(requestMessage);
        var deserialized = JsonSerializer.Deserialize<JsonRpcRequestMessage>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.JsonRpc.Should().Be("2.0");
        deserialized.Id.Should().Be("123");
        deserialized.Method.Should().Be("test_method");

        deserialized.Params.Should().NotBeNull();
        var paramJson = JsonSerializer.Serialize(deserialized.Params);
        var paramObj = JsonSerializer.Deserialize<JsonElement>(paramJson);
        paramObj.GetProperty("param1").GetString().Should().Be("value1");
        paramObj.GetProperty("param2").GetInt32().Should().Be(42);
    }

    [Fact]
    public void JsonRpcResponseMessage_Should_Serialize_Result_Correctly()
    {
        // Arrange
        var result = new { success = true, data = "test" };
        var responseMessage = new JsonRpcResponseMessage("2.0", "123", result, null);

        // Act
        var json = JsonSerializer.Serialize(responseMessage);
        var deserialized = JsonSerializer.Deserialize<JsonRpcResponseMessage>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.JsonRpc.Should().Be("2.0");
        deserialized.Id.Should().Be("123");
        deserialized.Error.Should().BeNull();
        deserialized.Result.Should().NotBeNull();

        // Convert result to JsonElement for testing
        var resultJsonString = JsonSerializer.Serialize(deserialized.Result);
        var resultElement = JsonSerializer.Deserialize<JsonElement>(resultJsonString);

        resultElement.GetProperty("success").GetBoolean().Should().BeTrue();
        resultElement.GetProperty("data").GetString().Should().Be("test");
    }

    [Fact]
    public void JsonRpcResponseMessage_Should_Serialize_Error_Correctly()
    {
        // Arrange
        var error = new JsonRpcError { Code = -32600, Message = "Invalid Request" };

        var responseMessage = new JsonRpcResponseMessage("2.0", "123", null, error);

        // Act
        var json = JsonSerializer.Serialize(responseMessage);
        var deserialized = JsonSerializer.Deserialize<JsonRpcResponseMessage>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.JsonRpc.Should().Be("2.0");
        deserialized.Id.Should().Be("123");
        deserialized.Result.Should().BeNull();
        deserialized.Error.Should().NotBeNull();
        deserialized.Error!.Code.Should().Be(-32600);
        deserialized.Error.Message.Should().Be("Invalid Request");
    }

    [Fact]
    public void JsonRpcNotificationMessage_Should_Serialize_Correctly()
    {
        // Arrange
        var notifyParams = new { param1 = "value1", param2 = 42 };
        var notificationMessage = new JsonRpcNotificationMessage(
            "2.0",
            "notify_method",
            notifyParams
        );

        // Act
        var json = JsonSerializer.Serialize(notificationMessage);
        var deserialized = JsonSerializer.Deserialize<JsonRpcNotificationMessage>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.JsonRpc.Should().Be("2.0");
        deserialized.Method.Should().Be("notify_method");

        deserialized.Params.Should().NotBeNull();
        var paramJson = JsonSerializer.Serialize(deserialized.Params);
        var paramObj = JsonSerializer.Deserialize<JsonElement>(paramJson);
        paramObj.GetProperty("param1").GetString().Should().Be("value1");
        paramObj.GetProperty("param2").GetInt32().Should().Be(42);
    }

    [Fact]
    public void JsonRpcRequestMessage_Should_Have_Correct_PropertyNames()
    {
        // Arrange and Act
        var json = JsonSerializer.Serialize(
            new JsonRpcRequestMessage("2.0", "123", "method", null)
        );

        // Assert - verify the serialized property names match JSON-RPC spec
        json.Should().Contain("\"jsonrpc\":\"2.0\"");
        json.Should().Contain("\"id\":\"123\"");
        json.Should().Contain("\"method\":\"method\"");
    }

    [Fact]
    public void JsonRpcResponseMessage_Should_Have_Correct_PropertyNames()
    {
        // Arrange and Act
        var json = JsonSerializer.Serialize(new JsonRpcResponseMessage("2.0", "123", null, null));

        // Assert - verify the serialized property names match JSON-RPC spec
        json.Should().Contain("\"jsonrpc\":\"2.0\"");
        json.Should().Contain("\"id\":\"123\"");
    }

    [Fact]
    public void JsonRpcResponseMessage_Should_Not_Serialize_Null_Values()
    {
        // Arrange
        var responseMessage = new JsonRpcResponseMessage("2.0", "123", null, null);

        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System
                .Text
                .Json
                .Serialization
                .JsonIgnoreCondition
                .WhenWritingNull,
        };

        // Act
        var json = JsonSerializer.Serialize(responseMessage, options);

        // Assert
        json.Should().NotContain("\"result\":null");
        json.Should().NotContain("\"error\":null");
    }

    [Fact]
    public void JsonRpcNotificationMessage_Should_Have_Correct_PropertyNames()
    {
        // Arrange and Act
        var json = JsonSerializer.Serialize(new JsonRpcNotificationMessage("2.0", "method", null));

        // Assert - verify the serialized property names match JSON-RPC spec
        json.Should().Contain("\"jsonrpc\":\"2.0\"");
        json.Should().Contain("\"method\":\"method\"");
        json.Should().NotContain("\"id\":");
    }

    [Fact]
    public void JsonRpcMessages_Should_RoundTrip_Meta()
    {
        var meta = new Dictionary<string, object?> { ["foo"] = "bar" };
        var request = new JsonRpcRequestMessage("2.0", "1", "test", null, meta);
        var serialized = JsonSerializer.Serialize(request);
        var parsed = JsonSerializer.Deserialize<JsonRpcRequestMessage>(serialized);
        parsed!.Meta.Should().NotBeNull();
        ((JsonElement)parsed.Meta!["foo"]!).GetString().Should().Be("bar");

        var response = new JsonRpcResponseMessage("2.0", "1", null, null, meta);
        serialized = JsonSerializer.Serialize(response);
        var parsedResponse = JsonSerializer.Deserialize<JsonRpcResponseMessage>(serialized);
        parsedResponse!.Meta.Should().NotBeNull();
        ((JsonElement)parsedResponse.Meta!["foo"]!).GetString().Should().Be("bar");

        var notification = new JsonRpcNotificationMessage("2.0", "event", null, meta);
        serialized = JsonSerializer.Serialize(notification);
        var parsedNotification = JsonSerializer.Deserialize<JsonRpcNotificationMessage>(serialized);
        parsedNotification!.Meta.Should().NotBeNull();
        ((JsonElement)parsedNotification.Meta!["foo"]!).GetString().Should().Be("bar");
    }

    [Fact]
    public void JsonRpcMessageParser_Should_Handle_Numeric_Ids_In_Requests()
    {
        // Arrange
        var parser = new JsonRpcMessageParser();
        var jsonWithNumericId =
            @"{
            ""jsonrpc"": ""2.0"",
            ""id"": 123,
            ""method"": ""test_method"",
            ""params"": {""param1"": ""value1""}
        }";

        // Act
        var message = parser.DeserializeRequest(jsonWithNumericId);

        // Assert
        message.Should().NotBeNull();
        message.Id.Should().Be("123");
        message.Method.Should().Be("test_method");
    }

    [Fact]
    public void JsonRpcMessageParser_Should_Handle_Numeric_Ids_In_Responses()
    {
        // Arrange
        var parser = new JsonRpcMessageParser();
        var jsonWithNumericId =
            @"{
            ""jsonrpc"": ""2.0"",
            ""id"": 456,
            ""result"": {""success"": true}
        }";

        // Act
        var message = parser.DeserializeResponse(jsonWithNumericId);

        // Assert - just verify the ID was parsed correctly
        message.Should().NotBeNull();
        message.Id.Should().Be("456");
        message.Result.Should().NotBeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(123456789)]
    [InlineData(0)]
    [InlineData(-123)]
    public void JsonRpcMessageParser_Should_Handle_Various_Numeric_Ids(int numericId)
    {
        // Arrange
        var parser = new JsonRpcMessageParser();
        var jsonWithNumericId =
            $@"{{
            ""jsonrpc"": ""2.0"",
            ""id"": {numericId},
            ""method"": ""test_method"",
            ""params"": {{""param1"": ""value1""}}
        }}";

        // Act
        var message = parser.DeserializeRequest(jsonWithNumericId);

        // Assert
        message.Should().NotBeNull();
        message.Id.Should().Be(numericId.ToString());
        message.Method.Should().Be("test_method");
    }

    [Fact]
    public void JsonRpcMessageParser_Should_Handle_Long_Numeric_Ids()
    {
        // Arrange
        var parser = new JsonRpcMessageParser();
        var jsonWithLongNumericId =
            @"{
            ""jsonrpc"": ""2.0"",
            ""id"": 9223372036854775807,
            ""method"": ""test_method"",
            ""params"": {""param1"": ""value1""}
        }";

        // Act
        var message = parser.DeserializeRequest(jsonWithLongNumericId);

        // Assert
        message.Should().NotBeNull();
        message.Id.Should().Be("9223372036854775807");
        message.Method.Should().Be("test_method");
    }

    [Fact]
    public void JsonRpcMessageParser_Should_Handle_Double_Numeric_Ids()
    {
        // Arrange
        var parser = new JsonRpcMessageParser();
        var jsonWithDoubleNumericId =
            @"{
            ""jsonrpc"": ""2.0"",
            ""id"": 123.456,
            ""method"": ""test_method"",
            ""params"": {""param1"": ""value1""}
        }";

        // Act
        var message = parser.DeserializeRequest(jsonWithDoubleNumericId);

        // Assert
        message.Should().NotBeNull();
        message.Id.Should().Be("123.456");
        message.Method.Should().Be("test_method");
    }
}
