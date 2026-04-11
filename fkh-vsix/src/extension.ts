import * as vscode from 'vscode';
import { createReadSettingsOptions, readSettings, getRepoName } from './readALGoSettings';
import { ProjectsTreeProvider, ProjectTreeItem, ContainersTreeProvider, ContainerTreeItem, ImagesTreeProvider, ImageTreeItem, VMsTreeProvider, VMTreeItem } from './containersTreeProvider';

let functionCatalog: FunctionCatalogResponse | undefined;
let outputChannel: vscode.OutputChannel;
let projectsProvider: ProjectsTreeProvider;
let containersProvider: ContainersTreeProvider;
let imagesProvider: ImagesTreeProvider;
let vmsProvider: VMsTreeProvider;

const containerLogContents = new Map<string, string>();
const containerLogProvider: vscode.TextDocumentContentProvider = {
  provideTextDocumentContent(uri: vscode.Uri): string {
    return containerLogContents.get(uri.toString()) ?? '';
  }
};

function getBackendUrl(): string | undefined {
  const url = vscode.workspace.getConfiguration('fkh').get<string>('backendUrl', '').trim();
  if (!url) {
    vscode.window.showErrorMessage(
      'Fkh: Backend URL is not configured. Set "fkh.backendUrl" in your settings.',
      'Open Settings'
    ).then((action: string | undefined) => {
      if (action === 'Open Settings') {
        vscode.commands.executeCommand('workbench.action.openSettings', 'fkh.backendUrl');
      }
    });
    return undefined;
  }
  return url;
}

export function activate(context: vscode.ExtensionContext) {
  outputChannel = vscode.window.createOutputChannel('Fkh');

  projectsProvider = new ProjectsTreeProvider(getRepoName, () => containersProvider.getContainers());
  const projectsView = vscode.window.createTreeView('fkhProjects', {
    treeDataProvider: projectsProvider,
  });

  containersProvider = new ContainersTreeProvider(getBackendUrl, getGitHubSession);
  const containersView = vscode.window.createTreeView('fkhContainers', {
    treeDataProvider: containersProvider,
    showCollapseAll: true,
  });

  imagesProvider = new ImagesTreeProvider(getBackendUrl, getGitHubSession);
  const imagesView = vscode.window.createTreeView('fkhImages', {
    treeDataProvider: imagesProvider,
    showCollapseAll: true,
  });

  vmsProvider = new VMsTreeProvider(getBackendUrl, getGitHubSession, () => containersProvider.getContainers());
  const vmsView = vscode.window.createTreeView('fkhVMs', {
    treeDataProvider: vmsProvider,
    showCollapseAll: true,
  });

  // Delay initial population to let the extension host settle
  setTimeout(async () => {
    await containersProvider.refresh();
    projectsProvider.refresh();
    imagesProvider.refresh();
    await vmsProvider.refresh();
    vscode.commands.executeCommand('setContext', 'fkh.isAdmin', vmsProvider.visible);
  }, 5000);

  context.subscriptions.push(
    outputChannel,
    projectsView,
    containersView,
    imagesView,
    vmsView,
    vscode.workspace.registerTextDocumentContentProvider('fkh-log', containerLogProvider),
    vscode.commands.registerCommand('fkh.refreshProjects', () => projectsProvider.refresh()),
    vscode.commands.registerCommand('fkh.refreshContainers', async () => {
      await containersProvider.refresh();
      projectsProvider.refresh();
    }),
    vscode.commands.registerCommand('fkh.refreshImages', () => imagesProvider.refresh()),
    vscode.commands.registerCommand('fkh.refreshVMs', async () => {
      await vmsProvider.refresh();
      vscode.commands.executeCommand('setContext', 'fkh.isAdmin', vmsProvider.visible);
    }),
    vscode.commands.registerCommand('fkh.getContainerLogs', async (item: ContainerTreeItem | ProjectTreeItem | VMTreeItem) => {
      if (!item.containerInfo) { return; }
      await showContainerLogs(item.containerInfo.appLabel, item.containerInfo.name);
    }),
    vscode.commands.registerCommand('fkh.startContainer', async (item: ContainerTreeItem | ProjectTreeItem) => {
      if (!item.containerInfo) { return; }
      await invokeContainerAction('StartContainer', item.containerInfo.name);
    }),
    vscode.commands.registerCommand('fkh.stopContainer', async (item: ContainerTreeItem | ProjectTreeItem) => {
      if (!item.containerInfo) { return; }
      await invokeContainerAction('StopContainer', item.containerInfo.name);
    }),
    vscode.commands.registerCommand('fkh.removeContainer', async (item: ContainerTreeItem | ProjectTreeItem) => {
      if (!item.containerInfo) { return; }
      const confirm = await vscode.window.showWarningMessage(
        `Are you sure you want to remove '${item.containerInfo.name}'? This will delete the container and its database.`,
        { modal: true },
        'Remove'
      );
      if (confirm !== 'Remove') { return; }
      await invokeContainerAction('RemoveContainer', item.containerInfo.name);
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
    vscode.commands.registerCommand('fkh.createContainer', () => createContainer()),
    vscode.commands.registerCommand('fkh.createContainerForProject', async (item: ProjectTreeItem) => {
      if (!item.projectName) { return; }
      await createContainer(item.projectName);
    }),
    vscode.commands.registerCommand('fkh.createImage', () => createImage()),
    vscode.commands.registerCommand('fkh.removeImage', async (item: ImageTreeItem) => {
      if (item.tagName && item.repositoryName) {
        // Tag-level item
        const confirm = await vscode.window.showWarningMessage(
          `Are you sure you want to remove tag '${item.tagName}' from '${item.repositoryName}'? This will delete the image tag and its associated database backup.`,
          { modal: true },
          'Remove'
        );
        if (confirm !== 'Remove') { return; }
        await invokeFunctionByName('RemoveImage', { repository: item.repositoryName, tag: item.tagName });
      } else if (item.imageInfo) {
        // Repository-level item
        const confirm = await vscode.window.showWarningMessage(
          `Are you sure you want to remove repository '${item.imageInfo.repository}' and all its tags?`,
          { modal: true },
          'Remove'
        );
        if (confirm !== 'Remove') { return; }
        await invokeFunctionByName('RemoveImage', { repository: item.imageInfo.repository });
      }
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

  const baseUrl = getBackendUrl();
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
        const url = `${getBackendUrl()}/${definition.route}`;

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

      await containersProvider.refresh();
      projectsProvider.refresh();
      imagesProvider.refresh();
    }
  );
}

async function invokeContainerAction(
  functionName: string,
  containerName: string,
): Promise<void> {
  const baseUrl = getBackendUrl();
  if (!baseUrl) { return; }

  const session = await getGitHubSession();
  if (!session) { return; }

  await vscode.window.withProgress(
    {
      location: vscode.ProgressLocation.Notification,
      title: `${functionName}: ${containerName}`,
      cancellable: false,
    },
    async () => {
      try {
        const body: FunctionInvokeRequest = { parameters: { name: containerName } };

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

      await containersProvider.refresh();
      projectsProvider.refresh();
    }
  );
}

async function showContainerLogs(appLabel: string, containerName: string): Promise<void> {
  const baseUrl = getBackendUrl();
  if (!baseUrl) { return; }

  const session = await getGitHubSession();
  if (!session) { return; }

  await vscode.window.withProgress(
    {
      location: vscode.ProgressLocation.Notification,
      title: `Fetching logs for ${containerName}...`,
      cancellable: false,
    },
    async () => {
      try {
        const body: FunctionInvokeRequest = { parameters: { name: appLabel } };
        const response = await fetch(`${baseUrl}/GetContainerLogs`, {
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
          const uri = vscode.Uri.parse(`fkh-log:${containerName}-${timestamp}.log`);
          containerLogContents.set(uri.toString(), result.message);
          const doc = await vscode.workspace.openTextDocument(uri);
          await vscode.window.showTextDocument(doc, { preview: true });
        } else {
          const error = response.status === 401 || response.status === 403
            ? `Access denied (${response.status}).`
            : `Failed (${response.status}): ${await response.text()}`;
          logOutput(`[GetContainerLogs] ${error}`, true);
        }
      } catch (err) {
        logOutput(`[GetContainerLogs] Could not reach the provisioning service: ${err instanceof Error ? err.message : String(err)}`, true);
      }
    }
  );
}

async function createContainer(project?: string): Promise<void> {
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

  const artifactUrl = artifact || `///${country}/latest`;

  outputChannel.appendLine('--- Resolved Settings ---');
  outputChannel.appendLine(`  Country: ${country}`);
  outputChannel.appendLine(`  Artifact: ${artifactUrl}${!artifact ? ' (defaulted)' : ''}`);
  outputChannel.show(true);

  await invokeFunctionByName('CreateContainer', { artifactUrl, repo: options.repoName, project: options.project || '' });
}

async function createImage(): Promise<void> {
  const session = await getGitHubSession();
  if (!session) { return; }

  const options = await createReadSettingsOptions(session.accessToken);
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
  if (!artifact) {
    vscode.window.showWarningMessage('Fkh: No artifact setting found in AL-Go settings.');
    return;
  }

  outputChannel.appendLine(`[CreateImage] Artifact: ${artifact}`);
  outputChannel.show(true);

  await invokeFunctionByName('CreateImage', { artifactUrl: artifact });
}

export function deactivate() {}
