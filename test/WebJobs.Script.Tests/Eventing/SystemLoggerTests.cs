﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SystemLoggerTests
    {
        private readonly SystemLogger _logger;
        private readonly Mock<IEventGenerator> _mockEventGenerator;
        private readonly string _websiteName;
        private readonly string _subscriptionId;
        private readonly ScriptSettingsManager _settingsManager;
        private readonly string _category;
        private readonly string _functionName = "TestFunction";
        private readonly string _hostInstanceId;

        public SystemLoggerTests()
        {
            _subscriptionId = "e3235165-1600-4819-85f0-2ab362e909e4";
            _hostInstanceId = Guid.NewGuid().ToString();
            _websiteName = "functionstest";

            _mockEventGenerator = new Mock<IEventGenerator>(MockBehavior.Strict);

            _settingsManager = new ScriptSettingsManager();
            _settingsManager.SetConfigurationFactory(() =>
            {
                var configurationBuilder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { EnvironmentSettingNames.AzureWebsiteOwnerName,  $"{_subscriptionId}+westuswebspace"},
                    { EnvironmentSettingNames.AzureWebsiteName,  _websiteName},
                });

                return configurationBuilder.Build();
            });

            _category = LogCategories.CreateFunctionCategory(_functionName);
            _logger = new SystemLogger(_hostInstanceId, _category, _mockEventGenerator.Object, _settingsManager);
        }

        [Fact]
        public void Trace_Verbose_EmitsExpectedEvent()
        {
            string eventName = string.Empty;
            string details = string.Empty;
            string summary = "TestMessage";
            string functionInvocationId = string.Empty;
            string activityId = string.Empty;

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Debug, _subscriptionId, _websiteName, _functionName, eventName, _category, details, summary, string.Empty, string.Empty, functionInvocationId, _hostInstanceId, activityId));

            _logger.LogDebug(summary);

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void Trace_Verbose_LogData_EmitsExpectedEvent()
        {
            string eventName = string.Empty;
            string details = string.Empty;
            string message = "TestMessage";
            string functionInvocationId = Guid.NewGuid().ToString();
            string activityId = Guid.NewGuid().ToString();
            var scopeState = new Dictionary<string, object>
            {
                [ScriptConstants.LogPropertyFunctionInvocationIdKey] = functionInvocationId
            };

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Debug, _subscriptionId, _websiteName, _functionName, eventName, _category, details, message, string.Empty, string.Empty, functionInvocationId, _hostInstanceId, activityId));

            var logData = new Dictionary<string, object>
            {
                [ScriptConstants.LogPropertyActivityIdKey] = activityId
            };

            using (_logger.BeginScope(scopeState))
            {
                _logger.Log(LogLevel.Debug, 0, logData, null, (state, ex) => message);
            }

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void Trace_Error_EmitsExpectedEvent()
        {
            string eventName = string.Empty;
            string message = "TestMessage";
            string functionInvocationId = string.Empty;
            string activityId = string.Empty;

            Exception ex = new Exception("Kaboom");

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Error, _subscriptionId, _websiteName, _functionName, eventName, _category, ex.ToFormattedString(), message, ex.GetType().ToString(), ex.Message, functionInvocationId, _hostInstanceId, activityId));

            _logger.LogError(ex, message);

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void Trace_Sanitizes()
        {
            string secretReplacement = "[Hidden Credential]";
            string secretString = "{ \"AzureWebJobsStorage\": \"DefaultEndpointsProtocol=https;AccountName=testAccount1;AccountKey=mykey1;EndpointSuffix=core.windows.net\", \"AnotherKey\": \"AnotherValue\" }";
            string sanitizedString = $"{{ \"AzureWebJobsStorage\": \"{secretReplacement}\", \"AnotherKey\": \"AnotherValue\" }}";

            string secretException = "Invalid string: \"DefaultEndpointsProtocol=https;AccountName=testaccount;AccountKey=testkey;BlobEndpoint=https://testaccount.blob.core.windows.net/;QueueEndpoint=https://testaccount.queue.core.windows.net/;TableEndpoint=https://testaccount.table.core.windows.net/;FileEndpoint=https://testaccount.file.core.windows.net/;\"";
            string sanitizedDetails = $"System.InvalidOperationException : Invalid string: \"{secretReplacement}\"";
            string sanitizedExceptionMessage = $"Invalid string: \"{secretReplacement}\"";

            string eventName = string.Empty;
            string functionInvocationId = string.Empty;
            string activityId = string.Empty;

            Exception ex = new InvalidOperationException(secretException);

            _mockEventGenerator.Setup(p => p.LogFunctionTraceEvent(LogLevel.Error, _subscriptionId, _websiteName, _functionName, eventName, _category, sanitizedDetails, sanitizedString, ex.GetType().ToString(), sanitizedExceptionMessage, functionInvocationId, _hostInstanceId, activityId));

            _logger.LogError(ex, secretString);

            _mockEventGenerator.VerifyAll();
        }

        [Fact]
        public void Trace_Ignores_FunctionUserCategory()
        {
            // Create a logger with the Function.{FunctionName}.User category, which is what determines user logs.
            ILogger logger = new SystemLogger(Guid.NewGuid().ToString(), LogCategories.CreateFunctionUserCategory(_functionName), _mockEventGenerator.Object, _settingsManager);
            logger.LogDebug("TestMessage");

            // Make sure it's never been called.
            _mockEventGenerator.Verify(p => p.LogFunctionTraceEvent(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty), Times.Never);
        }

        [Fact]
        public void Trace_Ignores_UserLogStateValue()
        {
            var logState = new Dictionary<string, object>
            {
                [ScriptConstants.LogPropertyIsUserLogKey] = true
            };

            // Try to pass the key via the state in two different ways. Neither should be logged.
            _logger.Log(LogLevel.Debug, 0, logState, null, (s, e) => "TestMessage");
            _logger.LogDebug($"{{{ScriptConstants.LogPropertyIsUserLogKey}}}", true);

            // Make sure it's never been called.
            _mockEventGenerator.Verify(p => p.LogFunctionTraceEvent(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty), Times.Never);
        }
    }
}