using System.Text.Json.Serialization;
using FControl.Models;

namespace FControl.Services;

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppConfiguration))]
internal sealed partial class AppConfigurationJsonContext : JsonSerializerContext
{
}
