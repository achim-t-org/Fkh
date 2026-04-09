using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BcArtifacts;

public sealed class ReadSettingsOptions
{
    public string BaseFolder { get; set; } = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? string.Empty;
    public string RepoName { get; set; } = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") ?? string.Empty;
    public string Project { get; set; } = ".";
    public string BuildMode { get; set; } = "Default";
    public string WorkflowName { get; set; } = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW") ?? string.Empty;
    public string UserName { get; set; } = Environment.GetEnvironmentVariable("GITHUB_ACTOR") ?? string.Empty;
    public string BranchName { get; set; } = Environment.GetEnvironmentVariable("GITHUB_REF_NAME") ?? string.Empty;
    public string OrgSettingsVariableValue { get; set; } = Environment.GetEnvironmentVariable("ALGoOrgSettings") ?? string.Empty;
    public string RepoSettingsVariableValue { get; set; } = Environment.GetEnvironmentVariable("ALGoRepoSettings") ?? string.Empty;
    public string EnvironmentSettingsVariableValue { get; set; } = Environment.GetEnvironmentVariable("ALGoEnvSettings") ?? string.Empty;
    public string EnvironmentName { get; set; } = Environment.GetEnvironmentVariable("ALGoEnvName") ?? string.Empty;
    public string CustomSettings { get; set; } = string.Empty;
}

public static class ReadALGoSettings
{
    public const string ALGoFolderName = ".AL-Go";
    public const string ALGoSettingsFileName = "settings.json";
    public const string RepoSettingsFileName = "AL-Go-Settings.json";
    public const string CustomTemplateRepoSettingsFileName = "AL-Go-TemplateRepoSettings.doNotEdit.json";
    public const string CustomTemplateProjectSettingsFileName = "AL-Go-TemplateProjectSettings.doNotEdit.json";

    public static string ALGoSettingsFile => Path.Combine(ALGoFolderName, ALGoSettingsFileName);
    public static string RepoSettingsFile => Path.Combine(".github", RepoSettingsFileName);
    public static string CustomTemplateRepoSettingsFile => Path.Combine(".github", CustomTemplateRepoSettingsFileName);
    public static string CustomTemplateProjectSettingsFile => Path.Combine(".github", CustomTemplateProjectSettingsFileName);

    public static JsonObject ReadSettings(ReadSettingsOptions? options = null)
    {
        options ??= new ReadSettingsOptions();

        if (string.IsNullOrWhiteSpace(options.BaseFolder))
            options.BaseFolder = Directory.GetCurrentDirectory();

        if (string.Equals(Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME"), "pull_request", StringComparison.OrdinalIgnoreCase)
            && string.Equals(options.BranchName, Environment.GetEnvironmentVariable("GITHUB_REF_NAME"), StringComparison.Ordinal))
        {
            options.BranchName = Environment.GetEnvironmentVariable("GITHUB_BASE_REF") ?? options.BranchName;
        }

        var repoName = NormalizeRepoName(options.RepoName);
        var workflowName = SanitizeWorkflowName(options.WorkflowName);
        var settings = GetDefaultSettings(repoName);

        var sources = BuildSettingsSources(options, workflowName);

        foreach (var source in sources)
        {
            if (source.Settings is null)
                continue;

            MergeInto(settings, source.Settings);

            if (TryGetPropertyValueIgnoreCase(source.Settings, "ConditionalSettings", out var conditionalNode)
                && conditionalNode is JsonArray conditionalArray)
            {
                foreach (var conditionalEntry in conditionalArray.OfType<JsonObject>())
                {
                    if (IsConditionalMatch(conditionalEntry, options, repoName, workflowName)
                        && TryGetPropertyValueIgnoreCase(conditionalEntry, "settings", out var conditionalSettingsNode)
                        && conditionalSettingsNode is JsonObject conditionalSettings)
                    {
                        MergeInto(settings, conditionalSettings);
                    }
                }
            }
        }

        PostProcessSettings(settings, options.Project);
        return settings;
    }

    public static string SanitizeWorkflowName(string workflowName)
    {
        if (string.IsNullOrWhiteSpace(workflowName))
            return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        return new string(workflowName.Trim().Where(ch => !invalid.Contains(ch)).ToArray());
    }

    private static bool TryGetPropertyValueIgnoreCase(JsonObject source, string propertyName, out JsonNode? value)
    {
        if (source.TryGetPropertyValue(propertyName, out value))
            return true;

        foreach (var pair in source)
        {
            if (string.Equals(pair.Key, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string? GetExistingKeyIgnoreCase(JsonObject source, string propertyName)
    {
        foreach (var pair in source)
        {
            if (string.Equals(pair.Key, propertyName, StringComparison.OrdinalIgnoreCase))
                return pair.Key;
        }

        return null;
    }

    private static string NormalizeRepoName(string repoName)
    {
        if (string.IsNullOrWhiteSpace(repoName))
            return string.Empty;

        var index = repoName.LastIndexOf('/');
        return index >= 0 ? repoName[(index + 1)..] : repoName;
    }

    private static List<(string Source, JsonObject? Settings)> BuildSettingsSources(ReadSettingsOptions options, string workflowName)
    {
        var result = new List<(string Source, JsonObject? Settings)>();
        var githubFolder = Path.Combine(options.BaseFolder, ".github");

        if (!string.IsNullOrWhiteSpace(options.OrgSettingsVariableValue))
            result.Add(("ALGoOrgSettings", ParseJsonObject(options.OrgSettingsVariableValue, "ALGoOrgSettings")));

        result.Add((CustomTemplateRepoSettingsFile, ReadSettingsObject(Path.Combine(options.BaseFolder, CustomTemplateRepoSettingsFile))));
        result.Add((RepoSettingsFile, ReadSettingsObject(Path.Combine(options.BaseFolder, RepoSettingsFile))));

        if (!string.IsNullOrWhiteSpace(options.RepoSettingsVariableValue))
            result.Add(("ALGoRepoSettings", ParseJsonObject(options.RepoSettingsVariableValue, "ALGoRepoSettings")));

        string? projectFolder = null;
        if (!string.IsNullOrWhiteSpace(options.Project))
        {
            projectFolder = Path.GetFullPath(Path.Combine(options.BaseFolder, options.Project));
            result.Add((CustomTemplateProjectSettingsFile, ReadSettingsObject(Path.Combine(options.BaseFolder, CustomTemplateProjectSettingsFile))));
            result.Add(($"{options.Project}/{ALGoSettingsFile}".Replace('\\', '/'), ReadSettingsObject(Path.Combine(projectFolder, ALGoSettingsFile))));
        }

        if (!string.IsNullOrWhiteSpace(workflowName))
        {
            result.Add(($".github/{workflowName}.settings.json", ReadSettingsObject(Path.Combine(githubFolder, $"{workflowName}.settings.json"))));

            if (!string.IsNullOrWhiteSpace(projectFolder))
            {
                result.Add(($"{options.Project}/{ALGoFolderName}/{workflowName}.settings.json".Replace('\\', '/'),
                    ReadSettingsObject(Path.Combine(projectFolder, ALGoFolderName, $"{workflowName}.settings.json"))));

                if (!string.IsNullOrWhiteSpace(options.UserName))
                {
                    result.Add(($"{options.Project}/{ALGoFolderName}/{options.UserName}.settings.json".Replace('\\', '/'),
                        ReadSettingsObject(Path.Combine(projectFolder, ALGoFolderName, $"{options.UserName}.settings.json"))));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(options.EnvironmentSettingsVariableValue))
            result.Add(($"ALGoEnvSettings for {options.EnvironmentName}", ParseJsonObject(options.EnvironmentSettingsVariableValue, "ALGoEnvSettings")));

        if (!string.IsNullOrWhiteSpace(options.CustomSettings))
            result.Add(("CustomSettings", ParseJsonObject(options.CustomSettings, "customSettings")));

        return result;
    }

    private static JsonObject? ReadSettingsObject(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
                return null;
            return ParseJsonObject(text, path);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error reading {path}. Error was {ex.Message}", ex);
        }
    }

    private static JsonObject ParseJsonObject(string json, string sourceName)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj)
                throw new InvalidOperationException($"{sourceName} does not contain a JSON object.");
            return obj;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to parse JSON from {sourceName}: {ex.Message}", ex);
        }
    }

    private static void MergeInto(JsonObject destination, JsonObject source)
    {
        if (TryGetPropertyValueIgnoreCase(source, "overwriteSettings", out var overwriteNode)
            && overwriteNode is JsonArray overwriteArray)
        {
            foreach (var item in overwriteArray.OfType<JsonValue>())
            {
                var key = item.TryGetValue<string>(out var keyValue) ? keyValue : null;
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var destinationKey = GetExistingKeyIgnoreCase(destination, key);
                var sourceKey = GetExistingKeyIgnoreCase(source, key);
                if (destinationKey is not null && sourceKey is not null)
                    destination.Remove(destinationKey);
            }
        }

        foreach (var pair in source)
        {
            if (string.Equals(pair.Key, "overwriteSettings", StringComparison.Ordinal))
                continue;

            var destinationKey = GetExistingKeyIgnoreCase(destination, pair.Key) ?? pair.Key;

            if (!destination.TryGetPropertyValue(destinationKey, out var dstNode) || dstNode is null)
            {
                destination[destinationKey] = pair.Value?.DeepClone();
                continue;
            }

            if (dstNode is JsonObject dstObj && pair.Value is JsonObject srcObj)
            {
                MergeInto(dstObj, srcObj);
                continue;
            }

            if (dstNode is JsonArray dstArray && pair.Value is JsonArray srcArray)
            {
                MergeArrays(dstArray, srcArray);
                continue;
            }

            destination[destinationKey] = pair.Value?.DeepClone();
        }
    }

    private static void MergeArrays(JsonArray destination, JsonArray source)
    {
        foreach (var srcItem in source)
        {
            if (srcItem is JsonObject)
            {
                destination.Add(srcItem?.DeepClone());
                continue;
            }

            var exists = destination.Any(dstItem => JsonNode.DeepEquals(dstItem, srcItem));
            if (!exists)
                destination.Add(srcItem?.DeepClone());
        }
    }

    private static bool IsConditionalMatch(JsonObject conditionalEntry, ReadSettingsOptions options, string repoName, string workflowName)
    {
        var checks = new Dictionary<string, string>
        {
            ["buildModes"] = options.BuildMode,
            ["branches"] = options.BranchName,
            ["repositories"] = repoName,
            ["projects"] = options.Project,
            ["workflows"] = workflowName,
            ["users"] = options.UserName,
        };

        foreach (var check in checks)
        {
            if (!TryGetPropertyValueIgnoreCase(conditionalEntry, check.Key, out var patternsNode) || patternsNode is not JsonArray patterns)
                continue;

            if (check.Key == "workflows")
            {
                patterns = new JsonArray(patterns.Select(p => JsonValue.Create(SanitizeWorkflowName(p?.GetValue<string>() ?? string.Empty))).ToArray());
            }

            if (string.IsNullOrWhiteSpace(check.Value))
                return false;

            var anyMatch = patterns
                .Select(p => p?.GetValue<string>() ?? string.Empty)
                .Any(pattern => WildcardLike(check.Value, pattern));

            if (!anyMatch)
                return false;
        }

        return true;
    }

    private static bool WildcardLike(string value, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return false;

        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(value ?? string.Empty, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static void PostProcessSettings(JsonObject settings, string project)
    {
        var runsOn = GetString(settings, "runs-on");
        var shell = GetString(settings, "shell");
        var githubRunner = GetString(settings, "githubRunner");
        var githubRunnerShell = GetString(settings, "githubRunnerShell");

        if (string.IsNullOrEmpty(shell))
            shell = runsOn.Contains("ubuntu-", StringComparison.OrdinalIgnoreCase) ? "pwsh" : "powershell";

        if (string.IsNullOrEmpty(githubRunner))
        {
            githubRunner = runsOn.Contains("ubuntu-", StringComparison.OrdinalIgnoreCase)
                ? "windows-latest"
                : runsOn;
        }

        if (string.IsNullOrEmpty(githubRunnerShell))
            githubRunnerShell = shell;

        if (!string.Equals(githubRunnerShell, "powershell", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(githubRunnerShell, "pwsh", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Invalid value for setting: gitHubRunnerShell: {githubRunnerShell}");
        }

        if (!string.Equals(shell, "powershell", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(shell, "pwsh", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Invalid value for setting: shell: {shell}");
        }

        if (githubRunner.Contains("ubuntu-", StringComparison.OrdinalIgnoreCase)
            && string.Equals(githubRunnerShell, "powershell", StringComparison.OrdinalIgnoreCase))
        {
            githubRunnerShell = "pwsh";
        }

        settings["shell"] = shell;
        settings["githubRunner"] = githubRunner;
        settings["githubRunnerShell"] = githubRunnerShell;

        if (string.IsNullOrEmpty(GetString(settings, "projectName")))
            settings["projectName"] = project;
    }

    private static string GetString(JsonObject source, string key, string fallback = "")
    {
        if (!TryGetPropertyValueIgnoreCase(source, key, out var node) || node is null)
            return fallback;

        if (node is JsonValue value && value.TryGetValue<string>(out var str))
            return str ?? fallback;

        return node.ToJsonString().Trim('"');
    }

    private static JsonObject GetDefaultSettings(string repoName)
    {
        return new JsonObject
        {
            ["type"] = "PTE",
            ["unusedALGoSystemFiles"] = new JsonArray(),
            ["projects"] = new JsonArray(),
            ["powerPlatformSolutionFolder"] = "",
            ["country"] = "us",
            ["artifact"] = "",
            ["companyName"] = "",
            ["repoVersion"] = "1.0",
            ["repoName"] = repoName,
            ["versioningStrategy"] = 0,
            ["runNumberOffset"] = 0,
            ["appBuild"] = 0,
            ["appRevision"] = 0,
            ["keyVaultName"] = "",
            ["licenseFileUrlSecretName"] = "licenseFileUrl",
            ["ghTokenWorkflowSecretName"] = "ghTokenWorkflow",
            ["adminCenterApiCredentialsSecretName"] = "adminCenterApiCredentials",
            ["applicationInsightsConnectionStringSecretName"] = "applicationInsightsConnectionString",
            ["keyVaultCertificateUrlSecretName"] = "keyVaultCertificateUrl",
            ["keyVaultCertificatePasswordSecretName"] = "keyVaultCertificatePassword",
            ["keyVaultClientIdSecretName"] = "keyVaultClientId",
            ["keyVaultCodesignCertificateName"] = "",
            ["codeSignCertificateUrlSecretName"] = "codeSignCertificateUrl",
            ["codeSignCertificatePasswordSecretName"] = "codeSignCertificatePassword",
            ["additionalCountries"] = new JsonArray(),
            ["appDependencies"] = new JsonArray(),
            ["projectName"] = "",
            ["appFolders"] = new JsonArray(),
            ["testDependencies"] = new JsonArray(),
            ["testFolders"] = new JsonArray(),
            ["bcptTestFolders"] = new JsonArray(),
            ["pageScriptingTests"] = new JsonArray(),
            ["restoreDatabases"] = new JsonArray(),
            ["installApps"] = new JsonArray(),
            ["installTestApps"] = new JsonArray(),
            ["installOnlyReferencedApps"] = true,
            ["runTestsInAllInstalledTestApps"] = false,
            ["generateDependencyArtifact"] = false,
            ["skipUpgrade"] = false,
            ["applicationDependency"] = "18.0.0.0",
            ["updateDependencies"] = false,
            ["installTestRunner"] = false,
            ["installTestFramework"] = false,
            ["installTestLibraries"] = false,
            ["installPerformanceToolkit"] = false,
            ["enableCodeCop"] = false,
            ["enableUICop"] = false,
            ["enableCodeAnalyzersOnTestApps"] = false,
            ["customCodeCops"] = new JsonArray(),
            ["trackALAlertsInGitHub"] = false,
            ["failOn"] = "error",
            ["treatTestFailuresAsWarnings"] = false,
            ["rulesetFile"] = "",
            ["enableExternalRulesets"] = false,
            ["vsixFile"] = "",
            ["assignPremiumPlan"] = false,
            ["enableTaskScheduler"] = false,
            ["doNotBuildTests"] = false,
            ["doNotRunTests"] = false,
            ["doNotRunBcptTests"] = false,
            ["doNotRunPageScriptingTests"] = false,
            ["doNotPublishApps"] = false,
            ["doNotSignApps"] = false,
            ["configPackages"] = new JsonArray(),
            ["appSourceCopMandatoryAffixes"] = new JsonArray(),
            ["deliverToAppSource"] = new JsonObject
            {
                ["mainAppFolder"] = "",
                ["productId"] = "",
                ["includeDependencies"] = new JsonArray(),
                ["continuousDelivery"] = false,
            },
            ["obsoleteTagMinAllowedMajorMinor"] = "",
            ["memoryLimit"] = "",
            ["templateUrl"] = "",
            ["templateSha"] = "",
            ["templateBranch"] = "",
            ["appDependencyProbingPaths"] = new JsonArray(),
            ["useProjectDependencies"] = false,
            ["runs-on"] = "windows-latest",
            ["shell"] = "",
            ["githubRunner"] = "",
            ["githubRunnerShell"] = "",
            ["cacheImageName"] = "my",
            ["cacheKeepDays"] = 3,
            ["alwaysBuildAllProjects"] = false,
            ["incrementalBuilds"] = new JsonObject
            {
                ["onPush"] = false,
                ["onPull_Request"] = true,
                ["onSchedule"] = false,
                ["retentionDays"] = 30,
                ["mode"] = "modifiedApps",
            },
            ["microsoftTelemetryConnectionString"] = "InstrumentationKey=cd2cc63e-0f37-4968-b99a-532411a314b8;IngestionEndpoint=https://northeurope-2.in.applicationinsights.azure.com/",
            ["partnerTelemetryConnectionString"] = "",
            ["sendExtendedTelemetryToMicrosoft"] = false,
            ["environments"] = new JsonArray(),
            ["buildModes"] = new JsonArray(),
            ["useCompilerFolder"] = false,
            ["pullRequestTrigger"] = "pull_request",
            ["bcptThresholds"] = new JsonObject
            {
                ["DurationWarning"] = 10,
                ["DurationError"] = 25,
                ["NumberOfSqlStmtsWarning"] = 5,
                ["NumberOfSqlStmtsError"] = 10,
            },
            ["fullBuildPatterns"] = new JsonArray(),
            ["excludeEnvironments"] = new JsonArray(),
            ["alDoc"] = new JsonObject
            {
                ["continuousDeployment"] = false,
                ["deployToGitHubPages"] = true,
                ["maxReleases"] = 3,
                ["groupByProject"] = true,
                ["includeProjects"] = new JsonArray(),
                ["excludeProjects"] = new JsonArray(),
                ["header"] = "Documentation for {REPOSITORY} {VERSION}",
                ["footer"] = "Documentation for <a href=\"https://github.com/{REPOSITORY}\">{REPOSITORY}</a> made with <a href=\"https://aka.ms/AL-Go\">AL-Go for GitHub</a>, <a href=\"https://go.microsoft.com/fwlink/?linkid=2247728\">ALDoc</a> and <a href=\"https://dotnet.github.io/docfx\">DocFx</a>",
                ["defaultIndexMD"] = "## Reference documentation\\n\\nThis is the generated reference documentation for [{REPOSITORY}](https://github.com/{REPOSITORY}).\\n\\nYou can use the navigation bar at the top and the table of contents to the left to navigate your documentation.\\n\\nYou can change this content by creating/editing the **{INDEXTEMPLATERELATIVEPATH}** file in your repository or use the alDoc:defaultIndexMD setting in your repository settings file (.github/AL-Go-Settings.json)\\n\\n{RELEASENOTES}",
                ["defaultReleaseMD"] = "## Release reference documentation\\n\\nThis is the generated reference documentation for [{REPOSITORY}](https://github.com/{REPOSITORY}).\\n\\nYou can use the navigation bar at the top and the table of contents to the left to navigate your documentation.\\n\\nYou can change this content by creating/editing the **{INDEXTEMPLATERELATIVEPATH}** file in your repository or use the alDoc:defaultReleaseMD setting in your repository settings file (.github/AL-Go-Settings.json)\\n\\n{RELEASENOTES}",
            },
            ["trustMicrosoftNuGetFeeds"] = true,
            ["nuGetFeedSelectMode"] = "LatestMatching",
            ["commitOptions"] = new JsonObject
            {
                ["messageSuffix"] = "",
                ["pullRequestAutoMerge"] = false,
                ["pullRequestMergeMethod"] = "squash",
                ["pullRequestLabels"] = new JsonArray(),
                ["createPullRequest"] = true,
            },
            ["trustedSigning"] = new JsonObject
            {
                ["Endpoint"] = "",
                ["Account"] = "",
                ["CertificateProfile"] = "",
            },
            ["useGitSubmodules"] = "false",
            ["gitSubmodulesTokenSecretName"] = "gitSubmodulesToken",
            ["shortLivedArtifactsRetentionDays"] = 1,
            ["reportSuppressedDiagnostics"] = false,
            ["workflowDefaultInputs"] = new JsonArray(),
            ["customALGoFiles"] = new JsonObject
            {
                ["filesToInclude"] = new JsonArray(),
                ["filesToExclude"] = new JsonArray(),
            },
            ["postponeProjectInBuildOrder"] = false,
        };
    }
}
