import * as vscode from 'vscode';
import { createReadSettingsOptions, readSettings, getRepoName } from './readALGoSettings';
import { determineArtifactUrl } from './bcArtifactHelper';
import { ProjectsTreeProvider, ProjectTreeItem, PodsTreeProvider, PodTreeItem, ImagesTreeProvider, NodesTreeProvider, NodeTreeItem } from './podsTreeProvider';

let functionCatalog: FunctionCatalogResponse | undefined;
let outputChannel: vscode.OutputChannel;
let projectsProvider: ProjectsTreeProvider;
let podsProvider: PodsTreeProvider;
let imagesProvider: ImagesTreeProvider;
let nodesProvider: NodesTreeProvider;

const podLogContents = new Map<string, string>();
const podLogProvider: vscode.TextDocumentContentProvider = {
  provideTextDocumentContent(uri: vscode.Uri): string {
    return podLogContents.get(uri.toString()) ?? '';
  }
};

function getBaseUrl(): string | undefined {
  const url = vscode.workspace.getConfiguration('fkh').get<string>('baseUrl', '').trim();
  if (!url) {
    vscode.window.showErrorMessage(
      'Fkh: Base URL is not configured. Set "fkh.baseUrl" in your settings.',
      'Open Settings'
    ).then((action: string | undefined) => {
      if (action === 'Open Settings') {
        vscode.commands.executeCommand('workbench.action.openSettings', 'fkh.baseUrl');
      }
    });
    return undefined;
  }
  return url;
}

export function activate(context: vscode.ExtensionContext) {
  outputChannel = vscode.window.createOutputChannel('Fkh');

  projectsProvider = new ProjectsTreeProvider(getRepoName, () => podsProvider.getPods());
  const projectsView = vscode.window.createTreeView('fkhProjects', {
    treeDataProvider: projectsProvider,
  });

  podsProvider = new PodsTreeProvider(getBaseUrl, getGitHubSession);
  const podsView = vscode.window.createTreeView('fkhPods', {
    treeDataProvider: podsProvider,
    showCollapseAll: true,
  });

  imagesProvider = new ImagesTreeProvider(getBaseUrl, getGitHubSession);
  const imagesView = vscode.window.createTreeView('fkhImages', {
    treeDataProvider: imagesProvider,
    showCollapseAll: true,
  });

  nodesProvider = new NodesTreeProvider(getBaseUrl, getGitHubSession, () => podsProvider.getPods());
  const nodesView = vscode.window.createTreeView('fkhNodes', {
    treeDataProvider: nodesProvider,
    showCollapseAll: true,
  });

  // Delay initial population to let the extension host settle
  setTimeout(async () => {
    await podsProvider.refresh();
    projectsProvider.refresh();
    imagesProvider.refresh();
    await nodesProvider.refresh();
    vscode.commands.executeCommand('setContext', 'fkh.isAdmin', nodesProvider.visible);
  }, 5000);

  context.subscriptions.push(
    outputChannel,
    projectsView,
    podsView,
    imagesView,
    nodesView,
    vscode.workspace.registerTextDocumentContentProvider('fkh-log', podLogProvider),
    vscode.commands.registerCommand('fkh.refreshProjects', () => projectsProvider.refresh()),
    vscode.commands.registerCommand('fkh.refreshPods', async () => {
      await podsProvider.refresh();
      projectsProvider.refresh();
    }),
    vscode.commands.registerCommand('fkh.refreshImages', () => imagesProvider.refresh()),
    vscode.commands.registerCommand('fkh.refreshNodes', async () => {
      await nodesProvider.refresh();
      vscode.commands.executeCommand('setContext', 'fkh.isAdmin', nodesProvider.visible);
    }),
    vscode.commands.registerCommand('fkh.getPodLogs', async (item: PodTreeItem | ProjectTreeItem | NodeTreeItem) => {
      if (!item.podInfo) { return; }
      await showPodLogs(item.podInfo.appLabel, item.podInfo.name);
    }),
    vscode.commands.registerCommand('fkh.startPod', async (item: PodTreeItem | ProjectTreeItem) => {
      if (!item.podInfo) { return; }
      await invokePodAction('StartPod', item.podInfo.name);
    }),
    vscode.commands.registerCommand('fkh.stopPod', async (item: PodTreeItem | ProjectTreeItem) => {
      if (!item.podInfo) { return; }
      await invokePodAction('StopPod', item.podInfo.name);
    }),
    vscode.commands.registerCommand('fkh.removePod', async (item: PodTreeItem | ProjectTreeItem) => {
      if (!item.podInfo) { return; }
      const confirm = await vscode.window.showWarningMessage(
        `Are you sure you want to remove '${item.podInfo.name}'? This will delete the pod and its database.`,
        { modal: true },
        'Remove'
      );
      if (confirm !== 'Remove') { return; }
      await invokePodAction('RemovePod', item.podInfo.name);
    }),
    vscode.commands.registerCommand('fkh.run', async () => {
      const catalog = await getFunctionCatalog();
      if (!catalog) { return; }

      const items = catalog.functions.map(f => ({
        label: `Fkh: ${f.name}`,
        description: f.description,
        functionName: f.name,
      }));

      const picked = await vscode.window.showQuickPick(items, {
        placeHolder: 'Select a command to run',
      });
      if (!picked) { return; }

      await invokeFunctionByName(picked.functionName);
    }),
    vscode.commands.registerCommand('fkh.createPod', () => createPod()),
    vscode.commands.registerCommand('fkh.createPodForProject', async (item: ProjectTreeItem) => {
      if (!item.projectName) { return; }
      await createPod(item.projectName);
    })
  );
}

type FunctionParameterDefinition = {
  name: string;
  type: string;
  description: string;
  required: boolean;
  defaultValue?: string;
};

type FunctionDefinition = {
  name: string;
  description: string;
  route: string;
  parameters: FunctionParameterDefinition[];
};

type FunctionCatalogResponse = {
  functions: FunctionDefinition[];
};

type FunctionInvokeRequest = {
  parameters: Record<string, string>;
};

async function getPublicIp(): Promise<string | undefined> {
  try {
    const response = await fetch('https://api.ipify.org?format=text');
    if (response.ok) {
      return (await response.text()).trim();
    }
  } catch { /* ignore */ }
  return undefined;
}

async function getGitHubSession(): Promise<vscode.AuthenticationSession | undefined> {
  try {
    // Try to get an existing session silently first (works in vscode.dev where
    // the user is already signed in via GitHub)
    const existing = await vscode.authentication.getSession(
      'github',
      ['read:user', 'read:org'],
      { createIfNone: false, silent: true }
    );
    if (existing) { return existing; }

    // Fall back: try without read:org — the backend will return 403 if
    // the token lacks permissions, but at least auth won't fail entirely
    const fallback = await vscode.authentication.getSession(
      'github',
      ['read:user'],
      { createIfNone: false, silent: true }
    );
    if (fallback) { return fallback; }

    // No silent session — prompt interactively
    return await vscode.authentication.getSession(
      'github',
      ['read:user', 'read:org'],
      { createIfNone: true }
    );
  } catch {
    vscode.window.showErrorMessage('GitHub sign-in was cancelled or failed.');
    return undefined;
  }
}

async function getFunctionCatalog(): Promise<FunctionCatalogResponse | undefined> {
  if (functionCatalog) {
    return functionCatalog;
  }

  const baseUrl = getBaseUrl();
  if (!baseUrl) { return undefined; }

  try {
    const response = await fetch(`${baseUrl}/functions`, { method: 'GET' });
    if (!response.ok) {
      const error = await response.text();
      vscode.window.showErrorMessage(`Failed to fetch function metadata: ${error}`);
      return undefined;
    }

    functionCatalog = await response.json() as FunctionCatalogResponse;
    return functionCatalog;
  } catch (err) {
    vscode.window.showErrorMessage(
      `Could not fetch function metadata: ${err instanceof Error ? err.message : String(err)}`
    );
    return undefined;
  }
}

async function promptForParameters(
  definition: FunctionDefinition,
  prefilled: Record<string, string> = {}
): Promise<Record<string, string> | undefined> {
  const parameters: Record<string, string> = { ...prefilled };
  const config = vscode.workspace.getConfiguration('fkh');

  for (const param of definition.parameters) {
    const prefilledKey = Object.keys(prefilled).find(
      k => k.toLowerCase() === param.name.toLowerCase()
    );
    if (prefilledKey) {
      continue;
    }

    const settingKey = `${definition.name}.${param.name}`;
    const settingValue = config.get<string>(settingKey, '').trim();
    if (settingValue) {
      parameters[param.name] = settingValue;
      continue;
    }

    let value: string | undefined = undefined;
    let defaultVal = param.defaultValue ?? '';

    // Auto-detect public IP for parameters named 'ip'
    if (param.name.toLowerCase() === 'ip') {
      const detectedIp = await getPublicIp();
      if (detectedIp) {
        defaultVal = detectedIp;
      }
    }

    while (true) {
      value = await vscode.window.showInputBox({
        prompt: `${param.name}: ${param.description} (Tip: set "fkh.${settingKey}" in settings to skip this prompt)`,
        placeHolder: defaultVal,
        value: defaultVal,
        password: param.name.toLowerCase().includes('password'),
        ignoreFocusOut: true,
      });

      // User cancelled input dialog.
      if (value === undefined) {
        return undefined;
      }

      if (value.trim().length > 0) {
        parameters[param.name] = value.trim();
        break;
      }

      if (!param.required) {
        break;
      }

      vscode.window.showWarningMessage(`${param.name} is required.`);
    }
  }

  return parameters;
}

function logOutput(message: string, isError = false): void {
  outputChannel.appendLine(message);
  outputChannel.show(true);
  if (isError) { vscode.window.showErrorMessage(message); }
}

async function invokeFunctionByName(functionName: string, prefilled: Record<string, string> = {}): Promise<void> {
  const catalog = await getFunctionCatalog();
  if (!catalog) { return; }

  const definition = catalog.functions.find(f => f.name.toLowerCase() === functionName.toLowerCase());
  if (!definition) {
    vscode.window.showErrorMessage(`Function '${functionName}' is not available.`);
    return;
  }

  const parameters = await promptForParameters(definition, prefilled);
  if (!parameters) { return; }

  const session = await getGitHubSession();
  if (!session) { return; }

  await vscode.window.withProgress(
    {
      location: vscode.ProgressLocation.Notification,
      title: `${definition.name}: ${definition.description}`,
      cancellable: true,
    },
    async (progress, cancelToken) => {
      try {
        const body: FunctionInvokeRequest = { parameters };
        const url = `${getBaseUrl()}/${definition.route}`;

        while (true) {
          if (cancelToken.isCancellationRequested) {
            logOutput(`[${definition.name}] Cancelled by user.`);
            return;
          }

          const response = await fetch(url, {
            method: 'POST',
            headers: {
              'Content-Type': 'application/json',
              Authorization: `Bearer ${session.accessToken}`,
            },
            body: JSON.stringify(body),
          });

          if (response.status === 202) {
            const result = await response.json() as { message: string; retryAfterSeconds?: number };
            const retrySeconds = result.retryAfterSeconds ?? parseInt(response.headers.get('Retry-After') ?? '0', 10);
            if (retrySeconds > 0) {
              progress.report({ message: result.message });
              logOutput(`[${definition.name}] ${result.message} Retrying in ${retrySeconds}s...`);
              await new Promise<void>((resolve) => {
                const timer = setTimeout(resolve, retrySeconds * 1000);
                cancelToken.onCancellationRequested(() => { clearTimeout(timer); resolve(); });
              });
              continue;
            }
          }

          if (response.ok) {
            const result = await response.json() as { message: string };
            logOutput(`[${definition.name}] ${result.message}`);
          } else {
            const error = response.status === 401 || response.status === 403
              ? `Access denied (${response.status}). Make sure your GitHub account is a member of an authorized team.`
              : `Failed (${response.status}): ${await response.text()}`;
            logOutput(`[${definition.name}] ${error}`, true);
          }
          break;
        }
      } catch (err) {
        logOutput(`[${definition.name}] Could not reach the provisioning service: ${err instanceof Error ? err.message : String(err)}`, true);
      }

      await podsProvider.refresh();
      projectsProvider.refresh();
    }
  );
}

async function invokePodAction(
  functionName: string,
  podName: string,
): Promise<void> {
  const baseUrl = getBaseUrl();
  if (!baseUrl) { return; }

  const session = await getGitHubSession();
  if (!session) { return; }

  await vscode.window.withProgress(
    {
      location: vscode.ProgressLocation.Notification,
      title: `${functionName}: ${podName}`,
      cancellable: false,
    },
    async () => {
      try {
        const body: FunctionInvokeRequest = { parameters: { name: podName } };

        const response = await fetch(`${baseUrl}/${functionName}`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            Authorization: `Bearer ${session.accessToken}`,
          },
          body: JSON.stringify(body),
        });

        if (response.ok) {
          const result = await response.json() as { message: string };
          logOutput(`[${functionName}] ${result.message}`);
        } else {
          const error = response.status === 401 || response.status === 403
            ? `Access denied (${response.status}).`
            : `Failed (${response.status}): ${await response.text()}`;
          logOutput(`[${functionName}] ${error}`, true);
        }
      } catch (err) {
        logOutput(`[${functionName}] Could not reach the provisioning service: ${err instanceof Error ? err.message : String(err)}`, true);
      }

      await podsProvider.refresh();
      projectsProvider.refresh();
    }
  );
}

async function showPodLogs(appLabel: string, podName: string): Promise<void> {
  const baseUrl = getBaseUrl();
  if (!baseUrl) { return; }

  const session = await getGitHubSession();
  if (!session) { return; }

  await vscode.window.withProgress(
    {
      location: vscode.ProgressLocation.Notification,
      title: `Fetching logs for ${podName}...`,
      cancellable: false,
    },
    async () => {
      try {
        const body: FunctionInvokeRequest = { parameters: { name: appLabel } };
        const response = await fetch(`${baseUrl}/GetPodLogs`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            Authorization: `Bearer ${session.accessToken}`,
          },
          body: JSON.stringify(body),
        });

        if (response.ok) {
          const result = await response.json() as { message: string };
          const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
          const uri = vscode.Uri.parse(`fkh-log:${podName}-${timestamp}.log`);
          podLogContents.set(uri.toString(), result.message);
          const doc = await vscode.workspace.openTextDocument(uri);
          await vscode.window.showTextDocument(doc, { preview: true });
        } else {
          const error = response.status === 401 || response.status === 403
            ? `Access denied (${response.status}).`
            : `Failed (${response.status}): ${await response.text()}`;
          logOutput(`[GetPodLogs] ${error}`, true);
        }
      } catch (err) {
        logOutput(`[GetPodLogs] Could not reach the provisioning service: ${err instanceof Error ? err.message : String(err)}`, true);
      }
    }
  );
}

async function createPod(project?: string): Promise<void> {
  const session = await getGitHubSession();
  if (!session) { return; }

  const options = await createReadSettingsOptions(session.accessToken, project);
  if (!options) { return; }
  if (!options.baseFolder) {
    vscode.window.showErrorMessage('Fkh: No Git repository found in the current workspace.');
    return;
  }

  let settings: Record<string, unknown>;
  try {
    settings = await readSettings(options);
  } catch (err) {
    vscode.window.showErrorMessage(`Fkh: Failed to read AL-Go settings: ${err instanceof Error ? err.message : String(err)}`);
    return;
  }

  const artifact = String(settings['artifact'] ?? '');
  const country = String(settings['country'] ?? 'us');

  outputChannel.appendLine('--- ReadSettings Options ---');
  outputChannel.appendLine(`  baseFolder: ${options.baseFolder.toString()}`);
  outputChannel.appendLine(`  repoName: ${options.repoName}`);
  outputChannel.appendLine(`  project: ${options.project || '(empty)'}`);
  outputChannel.appendLine(`  buildMode: ${options.buildMode}`);
  outputChannel.appendLine(`  workflowName: ${options.workflowName || '(empty)'}`);
  outputChannel.appendLine(`  userName: ${options.userName}`);
  outputChannel.appendLine(`  branchName: ${options.branchName}`);
  outputChannel.appendLine(`  orgSettingsVariableValue: ${options.orgSettingsVariableValue || '(empty)'}`);
  outputChannel.appendLine(`  repoSettingsVariableValue: ${options.repoSettingsVariableValue || '(empty)'}`);
  outputChannel.appendLine(`  environmentSettingsVariableValue: ${options.environmentSettingsVariableValue || '(empty)'}`);
  outputChannel.appendLine(`  environmentName: ${options.environmentName || '(empty)'}`);
  outputChannel.appendLine(`  customSettings: ${options.customSettings || '(empty)'}`);
  outputChannel.appendLine('--- Resolved Settings ---');
  outputChannel.appendLine(`  Country: ${country}`);
  outputChannel.appendLine(`  Artifact: ${artifact || '(not set)'}`);
  outputChannel.show(true);

  if (!artifact) {
    vscode.window.showWarningMessage('Fkh: No artifact setting found in AL-Go settings.');
    return;
  }

  let artifactUrl: string;
  try {
    artifactUrl = await determineArtifactUrl(settings);
    outputChannel.appendLine(`  ArtifactUrl: ${artifactUrl}`);
    outputChannel.show(true);
  } catch (err) {
    vscode.window.showErrorMessage(`Fkh: Failed to resolve artifact URL: ${err instanceof Error ? err.message : String(err)}`);
    return;
  }

  await invokeFunctionByName('CreatePod', { artifactUrl, repo: options.repoName, project: options.project || '' });
}

export function deactivate() {}
