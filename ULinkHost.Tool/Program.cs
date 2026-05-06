using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;

var exitCode = await ULinkHostToolCli.RunAsync(args).ConfigureAwait(false);
Environment.ExitCode = exitCode;

internal static class ULinkHostToolCli
{
    private static readonly string[] SupportedClientEngines = ["unity", "unity-cn", "tuanjie", "godot"];
    private static readonly string[] SupportedTransports = ["tcp", "websocket", "kcp"];
    private static readonly string[] SupportedSerializers = ["json", "memorypack"];
    private static readonly string[] SupportedNuGetForUnitySources = ["embedded", "openupm"];

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        return args[0] switch
        {
            "help" or "--help" or "-h" => HelpResult(),
            "new" or "init" => await NewAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
            "codegen" => await RegenerateCodeAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
            _ => UnknownCommand(args[0])
        };
    }

    private static int HelpResult()
    {
        PrintHelp();
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine();
        PrintHelp();
        return 1;
    }

    private static async Task<int> NewAsync(string[] args)
    {
        var options = ParseNewOptions(args);
        var outputDirectory = Path.GetFullPath(options.OutputPath ?? Path.Combine(Directory.GetCurrentDirectory(), "out"));
        Directory.CreateDirectory(outputDirectory);

        var projectName = string.IsNullOrWhiteSpace(options.Name) ? "MyGame" : options.Name;
        var projectRoot = Path.Combine(outputDirectory, projectName);

        var starterExitCode = await RunULinkRpcStarterNewAsync(projectName, outputDirectory, options).ConfigureAwait(false);
        if (starterExitCode != 0)
        {
            return starterExitCode;
        }

        if (!Directory.Exists(projectRoot))
        {
            Console.Error.WriteLine($"Generated project root not found: {projectRoot}");
            return 1;
        }

        await AugmentProjectWithULinkHostAsync(projectRoot, options).ConfigureAwait(false);

        var configPath = Path.Combine(projectRoot, "ulinkhost.tool.json");
        if (File.Exists(configPath))
        {
            Console.Error.WriteLine($"Config already exists: {configPath}");
            return 1;
        }

        var config = ToolConfig.CreateDefault(projectName, options);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(configPath, json).ConfigureAwait(false);
        Console.WriteLine($"Created tool config: {configPath}");
        return 0;
    }

    private static async Task<int> RegenerateCodeAsync(string[] args)
    {
        var options = ParseRegenerateCodeOptions(args);
        var configPath = options.ConfigPath ?? Path.Combine(Directory.GetCurrentDirectory(), "ulinkhost.tool.json");

        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Missing tool config: {configPath}");
            Console.Error.WriteLine("Run `ulinkhost-tool new` first or pass --config <path>.");
            return 1;
        }

        var config = await LoadConfigAsync(configPath).ConfigureAwait(false);
        var rootPath = Path.GetDirectoryName(Path.GetFullPath(configPath))
            ?? Directory.GetCurrentDirectory();

        var starterExitCode = await RunULinkRpcStarterCodegenAsync(rootPath, options.NoRestore).ConfigureAwait(false);
        if (starterExitCode != 0)
        {
            return starterExitCode;
        }

        Console.WriteLine("Code generation completed.");
        return 0;
    }

    private static RegenerateCodeOptions ParseRegenerateCodeOptions(string[] args)
    {
        string? configPath = null;
        var noRestore = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--no-restore":
                    noRestore = true;
                    break;
                case "--config" when index + 1 < args.Length:
                    configPath = Path.GetFullPath(args[++index]);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported option: {args[index]}");
            }
        }

        return new RegenerateCodeOptions(configPath, noRestore);
    }

    private static NewCommandOptions ParseNewOptions(string[] args)
    {
        string? name = null;
        string? outputPath = null;
        var clientEngine = "unity";
        var transport = "kcp";
        var serializer = "memorypack";
        var nuGetForUnitySource = "embedded";

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--name" when index + 1 < args.Length:
                    name = args[++index];
                    break;
                case "--output" when index + 1 < args.Length:
                    outputPath = args[++index];
                    break;
                case "--client-engine" when index + 1 < args.Length:
                    clientEngine = ValidateChoice("--client-engine", args[++index], SupportedClientEngines);
                    break;
                case "--transport" when index + 1 < args.Length:
                    transport = ValidateChoice("--transport", args[++index], SupportedTransports);
                    break;
                case "--serializer" when index + 1 < args.Length:
                    serializer = ValidateChoice("--serializer", args[++index], SupportedSerializers);
                    break;
                case "--nugetforunity-source" when index + 1 < args.Length:
                    nuGetForUnitySource = ValidateChoice("--nugetforunity-source", args[++index], SupportedNuGetForUnitySources);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported option: {args[index]}");
            }
        }

        return new NewCommandOptions(name, outputPath, clientEngine, transport, serializer, nuGetForUnitySource);
    }

    private static string ValidateChoice(string optionName, string value, IReadOnlyCollection<string> supportedValues)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (supportedValues.Contains(normalized))
        {
            return normalized;
        }

        throw new InvalidOperationException(
            $"Unsupported value '{value}' for {optionName}. Expected one of: {string.Join("|", supportedValues)}");
    }

    private static async Task<int> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    private static async Task<int> RunULinkRpcStarterNewAsync(string projectName, string outputDirectory, NewCommandOptions options)
    {
        var arguments = new[]
        {
            "new",
            "--name", projectName,
            "--output", outputDirectory,
            "--client-engine", options.ClientEngine,
            "--transport", options.Transport,
            "--serializer", options.Serializer,
            "--nugetforunity-source", options.NuGetForUnitySource
        };

        foreach (var invocation in EnumerateULinkRpcStarterInvocations(arguments))
        {
            try
            {
                return await RunProcessAsync(invocation.FileName, invocation.Arguments, Directory.GetCurrentDirectory()).ConfigureAwait(false);
            }
            catch (Win32Exception) when (invocation.CanFallback)
            {
            }
            catch (InvalidOperationException) when (invocation.CanFallback)
            {
            }
        }

        Console.Error.WriteLine("Unable to locate `ulinkrpc-starter`.");
        Console.Error.WriteLine("Install it globally or expose it on PATH before running `ulinkhost-tool new`.");
        return 1;
    }

    private static async Task<int> RunULinkRpcStarterCodegenAsync(string projectRoot, bool noRestore)
    {
        var arguments = new List<string>
        {
            "codegen",
            "--project-root", projectRoot
        };

        if (noRestore)
        {
            arguments.Add("--no-restore");
        }

        foreach (var invocation in EnumerateULinkRpcStarterInvocations(arguments))
        {
            try
            {
                return await RunProcessAsync(invocation.FileName, invocation.Arguments, Directory.GetCurrentDirectory()).ConfigureAwait(false);
            }
            catch (Win32Exception) when (invocation.CanFallback)
            {
            }
            catch (InvalidOperationException) when (invocation.CanFallback)
            {
            }
        }

        Console.Error.WriteLine("Unable to locate `ulinkrpc-starter`.");
        Console.Error.WriteLine("Install it globally or expose it on PATH before running `ulinkhost-tool codegen`.");
        return 1;
    }

    private static IEnumerable<ProcessInvocation> EnumerateULinkRpcStarterInvocations(IReadOnlyList<string> commandArguments)
    {
        yield return new ProcessInvocation("ulinkrpc-starter", commandArguments, true);
        yield return new ProcessInvocation("dotnet", ["tool", "run", "ulinkrpc-starter", "--", .. commandArguments], true);
    }

    private static async Task AugmentProjectWithULinkHostAsync(string projectRoot, NewCommandOptions options)
    {
        CopyULinkHostSource(projectRoot);
        await WriteServerSolutionAsync(projectRoot).ConfigureAwait(false);
        await WriteGatewayProgramAsync(projectRoot).ConfigureAwait(false);
        await WriteGatewayProjectAsync(projectRoot, options).ConfigureAwait(false);
        await WriteGatewayAppSettingsAsync(projectRoot, options).ConfigureAwait(false);
        await WriteGatewayConfiguratorsAsync(projectRoot, options).ConfigureAwait(false);
        await WriteSiloProjectAsync(projectRoot).ConfigureAwait(false);
        await WriteSiloProgramAsync(projectRoot).ConfigureAwait(false);
        await WriteSiloAppSettingsAsync(projectRoot).ConfigureAwait(false);
    }

    private static void CopyULinkHostSource(string projectRoot)
    {
        var templateRoot = ResolveTemplateRoot();
        var sourcePath = Path.Combine(templateRoot, "ULinkHost");
        var destinationPath = Path.Combine(projectRoot, "ULinkHost");

        CopyDirectory(sourcePath, destinationPath);
    }

    private static string ResolveTemplateRoot()
    {
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var candidate in candidates)
        {
            var current = new DirectoryInfo(candidate);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "ULinkHost", "ULinkHost.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new InvalidOperationException("Unable to locate local ULinkHost source templates.");
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, directory);
            if (IsBuildArtifactPath(relative))
            {
                continue;
            }

            Directory.CreateDirectory(Path.Combine(destinationPath, relative));
        }

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, file);
            if (IsBuildArtifactPath(relative))
            {
                continue;
            }

            var destinationFile = Path.Combine(destinationPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite: true);
        }
    }

    private static bool IsBuildArtifactPath(string relativePath)
    {
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(static segment =>
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static Task WriteServerSolutionAsync(string projectRoot)
    {
        const string content =
            """
            <Solution>
              <Project Path="../Shared/Shared.csproj" />
              <Project Path="../ULinkHost/ULinkHost.csproj" />
              <Project Path="Server/Server.csproj" />
              <Project Path="Silo/Silo.csproj" />
            </Solution>
            """;
        return File.WriteAllTextAsync(Path.Combine(projectRoot, "Server", "Server.slnx"), content + Environment.NewLine);
    }

    private static Task WriteGatewayProgramAsync(string projectRoot)
    {
        const string content =
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Hosting;
            using Server.Hosting;
            using ULinkHost.Hosting;

            var builder = Host.CreateApplicationBuilder(args);
            builder.Configuration
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();

            builder.AddULinkHostOrleansClient();
            builder.Services.AddSingleton(_ => new ControlPlaneRpcServerOptions(
                GatewayRpcServerOptions.FromConfiguration(
                    builder.Configuration,
                    "ControlPlane",
                    new GatewayRpcServerOptions { Transport = "websocket", Port = 20000, Path = "/ws" })));
            builder.Services.AddSingleton(_ => new RealtimeRpcServerOptions(
                GatewayRpcServerOptions.FromConfiguration(
                    builder.Configuration,
                    "Realtime",
                    new GatewayRpcServerOptions { Transport = "kcp", Port = 20001, Path = "" })));
            builder.Services.AddULinkRpcServer<DefaultControlPlaneRpcServerConfigurator>();
            builder.Services.AddULinkRpcServer<DefaultRealtimeRpcServerConfigurator>();
            builder.Services.AddULinkHostGateway();

            var host = builder.Build();
            await host.RunAsync();
            """;
        return File.WriteAllTextAsync(Path.Combine(projectRoot, "Server", "Server", "Program.cs"), content + Environment.NewLine);
    }

    private static Task WriteGatewayProjectAsync(string projectRoot, NewCommandOptions options)
    {
        var (serializerPackage, _) = GetSerializerArtifacts(options.Serializer);
        var (transportPackage, _) = GetTransportArtifacts(options.Transport);

        var content =
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <RootNamespace>Server</RootNamespace>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference Include="..\..\Shared\Shared.csproj" />
                <ProjectReference Include="..\..\ULinkHost\ULinkHost.csproj" />
              </ItemGroup>

              <ItemGroup>
                <PackageReference Include="ULinkRPC.Server" Version="0.11.7" />
                <PackageReference Include="{transportPackage.PackageId}" Version="{transportPackage.Version}" />
                <PackageReference Include="{serializerPackage.PackageId}" Version="{serializerPackage.Version}" />
                <PackageReference Include="Npgsql" Version="9.0.4" />
              </ItemGroup>

              <ItemGroup>
                <None Update="appsettings.json">
                  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
                </None>
              </ItemGroup>
            </Project>
            """;

        return File.WriteAllTextAsync(Path.Combine(projectRoot, "Server", "Server", "Server.csproj"), content + Environment.NewLine);
    }

    private static Task WriteGatewayAppSettingsAsync(string projectRoot, NewCommandOptions options)
    {
        var realtimePath = string.Equals(options.Transport, "websocket", StringComparison.OrdinalIgnoreCase) ? "/realtime" : "";
        var controlPlanePath = string.Equals(options.Transport, "websocket", StringComparison.OrdinalIgnoreCase) ? "/ws" : "";

        var content =
            $$"""
            {
              "Orleans": {
                "ClusterId": "dev",
                "ServiceId": "{{SanitizeJson(options.Name ?? "MyGame")}}-Server",
                "Invariant": "Npgsql",
                "ConnectionString": "Host=127.0.0.1;Port=5432;Database={{SanitizeJson((options.Name ?? "mygame").ToLowerInvariant())}};Username=postgres;Password=postgres"
              },
              "ControlPlane": {
                "Port": 20000,
                "Path": "{{SanitizeJson(controlPlanePath)}}"
              },
              "Realtime": {
                "Transport": "{{SanitizeJson(options.Transport)}}",
                "Host": "127.0.0.1",
                "Port": 20001,
                "Path": "{{SanitizeJson(realtimePath)}}"
              }
            }
            """;

        return File.WriteAllTextAsync(Path.Combine(projectRoot, "Server", "Server", "appsettings.json"), content + Environment.NewLine);
    }

    private static Task WriteGatewayConfiguratorsAsync(string projectRoot, NewCommandOptions options)
    {
        var hostingDirectory = Path.Combine(projectRoot, "Server", "Server", "Hosting");
        Directory.CreateDirectory(hostingDirectory);

        return Task.WhenAll(
            File.WriteAllTextAsync(Path.Combine(hostingDirectory, "GatewayRpcServerOptions.cs"), RenderGatewayRpcServerOptions() + Environment.NewLine),
            File.WriteAllTextAsync(Path.Combine(hostingDirectory, "ControlPlaneRpcServerOptions.cs"), RenderNamedRpcServerOptions("ControlPlaneRpcServerOptions") + Environment.NewLine),
            File.WriteAllTextAsync(Path.Combine(hostingDirectory, "RealtimeRpcServerOptions.cs"), RenderNamedRpcServerOptions("RealtimeRpcServerOptions") + Environment.NewLine),
            File.WriteAllTextAsync(Path.Combine(hostingDirectory, "DefaultControlPlaneRpcServerConfigurator.cs"), RenderControlPlaneConfigurator(options) + Environment.NewLine),
            File.WriteAllTextAsync(Path.Combine(hostingDirectory, "DefaultRealtimeRpcServerConfigurator.cs"), RenderRealtimeConfigurator(options) + Environment.NewLine));
    }

    private static string RenderGatewayRpcServerOptions()
    {
        return @"using Microsoft.Extensions.Configuration;

namespace Server.Hosting;

internal sealed class GatewayRpcServerOptions
{
    public string Transport { get; init; } = ""websocket"";
    public string Host { get; init; } = ""127.0.0.1"";
    public int Port { get; init; } = 20000;
    public string Path { get; init; } = """";

    public static GatewayRpcServerOptions FromConfiguration(
        IConfiguration configuration,
        string sectionName,
        GatewayRpcServerOptions defaults)
    {
        var section = configuration.GetSection(sectionName);
        var transport = NormalizeTransport(section[""Transport""], defaults.Transport);
        var host = section[""Host""];
        var path = section[""Path""];

        return new GatewayRpcServerOptions
        {
            Transport = transport,
            Host = string.IsNullOrWhiteSpace(host) ? defaults.Host : host,
            Port = ParsePort(section[""Port""], defaults.Port),
            Path = string.IsNullOrWhiteSpace(path) ? defaults.Path : path
        };
    }

    private static string NormalizeTransport(string? rawValue, string fallback)
    {
        return string.IsNullOrWhiteSpace(rawValue)
            ? fallback
            : rawValue.Trim().ToLowerInvariant();
    }

    private static int ParsePort(string? rawValue, int fallback)
    {
        return int.TryParse(rawValue, out var port) && port > 0
            ? port
            : fallback;
    }
}";
    }

    private static string RenderNamedRpcServerOptions(string typeName)
    {
        return $@"namespace Server.Hosting;

internal sealed class {typeName}
{{
    public {typeName}(GatewayRpcServerOptions endpoint)
    {{
        Endpoint = endpoint;
    }}

    public GatewayRpcServerOptions Endpoint {{ get; }}
}}";
    }

    private static Task WriteSiloProjectAsync(string projectRoot)
    {
        const string content =
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference Include="..\..\ULinkHost\ULinkHost.csproj" />
                <PackageReference Include="Microsoft.Orleans.Clustering.AdoNet" Version="10.0.0" />
                <PackageReference Include="Microsoft.Orleans.Persistence.AdoNet" Version="10.0.0" />
                <PackageReference Include="Microsoft.Orleans.Server" Version="10.0.0" />
                <PackageReference Include="Npgsql" Version="9.0.4" />
              </ItemGroup>

              <ItemGroup>
                <None Update="appsettings.json">
                  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
                </None>
              </ItemGroup>
            </Project>
            """;

        var siloDirectory = Path.Combine(projectRoot, "Server", "Silo");
        Directory.CreateDirectory(siloDirectory);
        return File.WriteAllTextAsync(Path.Combine(siloDirectory, "Silo.csproj"), content + Environment.NewLine);
    }

    private static Task WriteSiloProgramAsync(string projectRoot)
    {
        const string content =
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.Hosting;
            using Orleans.Hosting;
            using ULinkHost.Hosting;

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(configuration =>
                {
                    configuration
                        .SetBasePath(AppContext.BaseDirectory)
                        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                        .AddEnvironmentVariables();
                })
                .UseULinkHostOrleansSilo((context, silo) =>
                {
                    var configuration = context.Configuration;
                    var invariant = configuration["Orleans:Invariant"] ?? "Npgsql";
                    var connectionString = configuration["Orleans:ConnectionString"]
                        ?? throw new InvalidOperationException("Missing configuration: Orleans:ConnectionString");

                    silo.AddAdoNetGrainStorage("users", options =>
                    {
                        options.Invariant = invariant;
                        options.ConnectionString = connectionString;
                    });
                    silo.AddAdoNetGrainStorage("sessions", options =>
                    {
                        options.Invariant = invariant;
                        options.ConnectionString = connectionString;
                    });
                    silo.AddAdoNetGrainStorage("matchmaking", options =>
                    {
                        options.Invariant = invariant;
                        options.ConnectionString = connectionString;
                    });
                    silo.AddAdoNetGrainStorage("rooms", options =>
                    {
                        options.Invariant = invariant;
                        options.ConnectionString = connectionString;
                    });
                })
                .Build();

            await host.RunAsync();
            """;
        return File.WriteAllTextAsync(Path.Combine(projectRoot, "Server", "Silo", "Program.cs"), content + Environment.NewLine);
    }

    private static Task WriteSiloAppSettingsAsync(string projectRoot)
    {
        const string content =
            """
            {
              "Orleans": {
                "ClusterId": "dev",
                "ServiceId": "ULinkHost-Server",
                "Invariant": "Npgsql",
                "ConnectionString": "Host=127.0.0.1;Port=5432;Database=ulinkhost;Username=postgres;Password=postgres",
                "SiloPort": 11111,
                "GatewayPort": 30000
              }
            }
            """;
        return File.WriteAllTextAsync(Path.Combine(projectRoot, "Server", "Silo", "appsettings.json"), content + Environment.NewLine);
    }

    private static string RenderControlPlaneConfigurator(NewCommandOptions options)
    {
        var (serializerPackage, serializerType) = GetSerializerArtifacts(options.Serializer);
        var (transportPackage, _) = GetTransportArtifacts(options.Transport);

        return $@"using ULinkHost.Hosting;
using {serializerPackage.Namespace};
using {transportPackage.Namespace};

namespace Server.Hosting;

internal sealed class DefaultControlPlaneRpcServerConfigurator : IULinkRpcServerConfigurator
{{
    private readonly GatewayRpcServerOptions _options;

    public DefaultControlPlaneRpcServerConfigurator(ControlPlaneRpcServerOptions options)
    {{
        _options = options.Endpoint;
    }}

    public string Name => ""control"";

    public void Configure(ULinkHostRpcServerContext context)
    {{
        var builder = context.Builder;
        builder.UseSerializer(new {serializerType}());
{IndentBlock(RenderControlPlaneAcceptor(options.Transport), 2)}
    }}
}}";
    }

    private static string RenderRealtimeConfigurator(NewCommandOptions options)
    {
        var (serializerPackage, serializerType) = GetSerializerArtifacts(options.Serializer);
        var (transportPackage, _) = GetTransportArtifacts(options.Transport);

        return $@"using ULinkHost.Hosting;
using {serializerPackage.Namespace};
using {transportPackage.Namespace};

namespace Server.Hosting;

internal sealed class DefaultRealtimeRpcServerConfigurator : IULinkRpcServerConfigurator
{{
    private readonly GatewayRpcServerOptions _options;

    public DefaultRealtimeRpcServerConfigurator(RealtimeRpcServerOptions options)
    {{
        _options = options.Endpoint;
    }}

    public string Name => ""realtime"";

    public void Configure(ULinkHostRpcServerContext context)
    {{
        var builder = context.Builder;
        builder.UseSerializer(new {serializerType}());
{IndentBlock(RenderRealtimeAcceptor(options.Transport), 2)}
    }}
}}";
    }

    private static string RenderControlPlaneAcceptor(string transport)
    {
        return transport switch
        {
            "websocket" => """
                var path = string.IsNullOrWhiteSpace(_options.Path) ? "/ws" : _options.Path;
                builder.UseAcceptor(async ct => await WsConnectionAcceptor.CreateAsync(
                    builder.ResolvePort(_options.Port),
                    path,
                    builder.Limits.MaxPendingAcceptedConnections,
                    ct));
                """,
            "tcp" => """
                builder.UseAcceptor(new TcpConnectionAcceptor(builder.ResolvePort(_options.Port)));
                """,
            _ => """
                builder.UseAcceptor(new KcpConnectionAcceptor(
                    builder.ResolvePort(_options.Port),
                    builder.Limits.MaxPendingAcceptedConnections));
                """
        };
    }

    private static string RenderRealtimeAcceptor(string transport)
    {
        return transport switch
        {
            "websocket" => """
                var path = string.IsNullOrWhiteSpace(_options.Path) ? "/realtime" : _options.Path;
                builder.UseAcceptor(async ct => await WsConnectionAcceptor.CreateAsync(
                    builder.ResolvePort(_options.Port),
                    path,
                    builder.Limits.MaxPendingAcceptedConnections,
                    ct));
                """,
            "tcp" => """
                builder.UseAcceptor(new TcpConnectionAcceptor(builder.ResolvePort(_options.Port)));
                """,
            _ => """
                builder.UseAcceptor(new KcpConnectionAcceptor(
                    builder.ResolvePort(_options.Port),
                    builder.Limits.MaxPendingAcceptedConnections));
                """
        };
    }

    private static (PackageArtifact PackageId, string SerializerType) GetSerializerArtifacts(string serializer)
    {
        return serializer switch
        {
            "json" => (new PackageArtifact("ULinkRPC.Serializer.Json", "0.11.0", "ULinkRPC.Serializer.Json"), "JsonRpcSerializer"),
            _ => (new PackageArtifact("ULinkRPC.Serializer.MemoryPack", "0.11.0", "ULinkRPC.Serializer.MemoryPack"), "MemoryPackRpcSerializer")
        };
    }

    private static (PackageArtifact PackageId, string AcceptorType) GetTransportArtifacts(string transport)
    {
        return transport switch
        {
            "tcp" => (new PackageArtifact("ULinkRPC.Transport.Tcp", "0.11.2", "ULinkRPC.Transport.Tcp"), "TcpConnectionAcceptor"),
            "websocket" => (new PackageArtifact("ULinkRPC.Transport.WebSocket", "0.11.3", "ULinkRPC.Transport.WebSocket"), "WsConnectionAcceptor"),
            _ => (new PackageArtifact("ULinkRPC.Transport.Kcp", "0.11.8", "ULinkRPC.Transport.Kcp"), "KcpConnectionAcceptor")
        };
    }

    private static string SanitizeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string IndentBlock(string block, int level)
    {
        var indent = new string(' ', level * 4);
        var lines = block.Replace("\r\n", "\n").Split('\n');
        return string.Join(Environment.NewLine, lines.Select(line => string.IsNullOrWhiteSpace(line) ? string.Empty : indent + line));
    }

    private static async Task<ToolConfig> LoadConfigAsync(string configPath)
    {
        await using var stream = File.OpenRead(configPath);
        var config = await JsonSerializer.DeserializeAsync<ToolConfig>(stream, JsonOptions).ConfigureAwait(false);
        return config ?? throw new InvalidOperationException($"Failed to parse tool config: {configPath}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            ULinkHost.Tool

            Commands:
              new [--name MyGame] [--output ./out] [--client-engine unity|unity-cn|tuanjie|godot] [--transport tcp|websocket|kcp] [--serializer json|memorypack] [--nugetforunity-source embedded|openupm]
                  Generate a ULinkRPC project via ulinkrpc-starter, then augment it with ULinkHost and Microsoft Orleans.

              codegen [--config <path>] [--no-restore]
                  Delegate code generation to ulinkrpc-starter codegen.
            """);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly record struct RegenerateCodeOptions(string? ConfigPath, bool NoRestore);
    private readonly record struct ProcessInvocation(string FileName, IReadOnlyList<string> Arguments, bool CanFallback);
    private readonly record struct PackageArtifact(string PackageId, string Version, string Namespace);
}

internal sealed class ToolConfig
{
    public ProjectConfig Project { get; set; } = new();
    public CodegenConfig Codegen { get; set; } = new();

    public static ToolConfig CreateDefault(string projectName, NewCommandOptions options)
    {
        return new ToolConfig
        {
            Project = new ProjectConfig
            {
                Name = projectName,
                ClientEngine = options.ClientEngine,
                Transport = options.Transport,
                Serializer = options.Serializer,
                NuGetForUnitySource = options.NuGetForUnitySource
            },
            Codegen = new CodegenConfig
            {
                ContractsPath = "Shared",
                Server = new CodegenTargetConfig
                {
                    ProjectPath = "Server/Server",
                    OutputPath = "Generated",
                    Namespace = "Server.Generated"
                },
                UnityClient = new CodegenTargetConfig
                {
                    ProjectPath = "Client",
                    OutputPath = "Assets/Scripts/Rpc/Generated",
                    Namespace = "Rpc.Generated"
                }
            }
        };
    }
}

internal sealed class CodegenConfig
{
    public string ContractsPath { get; set; } = "Shared";
    public CodegenTargetConfig? Server { get; set; }
    public CodegenTargetConfig? UnityClient { get; set; }
}

internal sealed class ProjectConfig
{
    public string Name { get; set; } = "MyGame";
    public string ClientEngine { get; set; } = "unity";
    public string Transport { get; set; } = "kcp";
    public string Serializer { get; set; } = "memorypack";
    public string NuGetForUnitySource { get; set; } = "embedded";
}

internal readonly record struct NewCommandOptions(
    string? Name,
    string? OutputPath,
    string ClientEngine,
    string Transport,
    string Serializer,
    string NuGetForUnitySource);

internal sealed class CodegenTargetConfig
{
    public string ProjectPath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string Namespace { get; set; } = "";
}
