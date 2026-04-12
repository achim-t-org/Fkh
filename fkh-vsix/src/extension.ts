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
    vscode.commands.registerCommand('fkh.extendAutoStop', async (item: ContainerTreeItem | ProjectTreeItem) => {
      if (!item.containerInfo) { return; }
      await invokeContainerAction('ExtendAutoStop', item.containerInfo.name);
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
  const config = vscode.workspace.getConfiguration('fkh');

  // Resolve defaults: prefilled > settings > auto-detect > catalog default
  const resolvedDefaults: Record<string, string> = {};
  const promptParams: FunctionParameterDefinition[] = [];

  for (const param of definition.parameters) {
    const prefilledKey = Object.keys(prefilled).find(
      k => k.toLowerCase() === param.name.toLowerCase()
    );
    if (prefilledKey) {
      resolvedDefaults[param.name] = prefilled[prefilledKey];
      continue;
    }

    const settingKey = `${definition.name}.${param.name}`;
    const settingValue = config.get<string>(settingKey, '').trim();
    if (settingValue) {
      resolvedDefaults[param.name] = settingValue;
    }

    // Auto-detect public IP for parameters named 'ip'
    if (param.name.toLowerCase() === 'ip') {
      const detectedIp = await getPublicIp();
      if (detectedIp) {
        resolvedDefaults[param.name] = detectedIp;
      }
    }

    promptParams.push(param);
  }

  // If nothing to prompt for, return immediately
  if (promptParams.length === 0) {
    return resolvedDefaults;
  }

  // Single parameter: use simple input box
  if (promptParams.length === 1) {
    const param = promptParams[0];
    const defaultVal = resolvedDefaults[param.name] ?? param.defaultValue ?? '';
    const settingKey = `${definition.name}.${param.name}`;
    const isPassword = param.name.toLowerCase().includes('password');

    while (true) {
      const value = await vscode.window.showInputBox({
        prompt: `${param.name}: ${param.description} (Tip: set "fkh.${settingKey}" in settings to skip this prompt)`,
        placeHolder: defaultVal,
        value: defaultVal,
        password: isPassword,
        ignoreFocusOut: true,
      });

      if (value === undefined) {
        return undefined;
      }

      if (value.trim().length > 0) {
        resolvedDefaults[param.name] = value.trim();
        return resolvedDefaults;
      }

      if (!param.required) {
        return resolvedDefaults;
      }

      vscode.window.showWarningMessage(`${param.name} is required.`);
    }
  }

  // Multiple parameters: use webview form

  return new Promise<Record<string, string> | undefined>((resolve) => {
    const panel = vscode.window.createWebviewPanel(
      'fkhParameters',
      `${definition.name}`,
      vscode.ViewColumn.Active,
      { enableScripts: true }
    );

    let resolved = false;

    panel.webview.onDidReceiveMessage((msg: { command: string; parameters?: Record<string, string> }) => {
      if (msg.command === 'submit' && msg.parameters) {
        resolved = true;
        const result = { ...resolvedDefaults };
        for (const [key, value] of Object.entries(msg.parameters)) {
          if (value.trim().length > 0) {
            result[key] = value.trim();
          }
        }
        panel.dispose();
        resolve(result);
      } else if (msg.command === 'cancel') {
        resolved = true;
        panel.dispose();
        resolve(undefined);
      }
    });

    panel.onDidDispose(() => {
      if (!resolved) { resolve(undefined); }
    });

    const fieldsHtml = promptParams.map(param => {
      const defaultVal = resolvedDefaults[param.name] ?? param.defaultValue ?? '';
      const escapedDefault = defaultVal.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
      const escapedDesc = param.description.replace(/&/g, '&amp;').replace(/</g, '&lt;');
      const requiredBadge = param.required ? '<span class="required">required</span>' : '';
      const isPassword = param.name.toLowerCase().includes('password');

      if (param.type.toLowerCase() === 'boolean') {
        const checked = defaultVal.toLowerCase() === 'true' ? 'checked' : '';
        return `<div class="field">
          <label><input type="checkbox" name="${param.name}" ${checked} /> ${param.name} ${requiredBadge}</label>
          <div class="desc">${escapedDesc}</div>
        </div>`;
      }

      return `<div class="field">
        <label for="${param.name}">${param.name} ${requiredBadge}</label>
        <div class="desc">${escapedDesc}</div>
        <input type="${isPassword ? 'password' : 'text'}" id="${param.name}" name="${param.name}"
          value="${escapedDefault}" placeholder="${escapedDefault || (param.required ? '(required)' : '(optional)')}"${param.required ? ' required' : ''} />
      </div>`;
    }).join('\n');

    panel.webview.html = `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline';">
<style>
  body { font-family: var(--vscode-font-family, sans-serif); padding: 16px; color: var(--vscode-foreground); background: var(--vscode-editor-background); }
  h2 { margin: 0 0 4px 0; }
  .subtitle { color: var(--vscode-descriptionForeground); margin-bottom: 16px; }
  .field { margin-bottom: 14px; }
  label { font-weight: 600; display: block; margin-bottom: 2px; }
  .desc { color: var(--vscode-descriptionForeground); font-size: 12px; margin-bottom: 4px; }
  .required { color: var(--vscode-errorForeground); font-size: 11px; font-weight: normal; }
  input[type="text"], input[type="password"] {
    width: 100%; box-sizing: border-box; padding: 6px 8px;
    background: var(--vscode-input-background); color: var(--vscode-input-foreground);
    border: 1px solid var(--vscode-input-border, transparent); border-radius: 2px;
  }
  input[type="text"]:focus, input[type="password"]:focus { outline: 1px solid var(--vscode-focusBorder); }
  input:invalid { border-color: var(--vscode-errorForeground); }
  .buttons { margin-top: 20px; display: flex; gap: 8px; }
  button {
    padding: 6px 16px; border: none; border-radius: 2px; cursor: pointer; font-size: 13px;
  }
  .btn-primary { background: var(--vscode-button-background); color: var(--vscode-button-foreground); }
  .btn-primary:hover { background: var(--vscode-button-hoverBackground); }
  .btn-secondary { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
  .btn-secondary:hover { background: var(--vscode-button-secondaryHoverBackground); }
</style>
</head>
<body>
  <h2>${definition.name}</h2>
  <div class="subtitle">${definition.description}</div>
  <form id="paramForm">
    ${fieldsHtml}
    <div class="buttons">
      <button type="submit" class="btn-primary">Run</button>
      <button type="button" class="btn-secondary" id="cancelBtn">Cancel</button>
    </div>
  </form>
  <script>
    const vscode = acquireVsCodeApi();
    const form = document.getElementById('paramForm');
    form.addEventListener('submit', (e) => {
      e.preventDefault();
      const params = {};
      const inputs = form.querySelectorAll('input');
      for (const el of inputs) {
        if (!el.getAttribute('name')) continue;
        const paramName = el.getAttribute('name');
        if (el.type === 'checkbox') {
          params[paramName] = el.checked ? 'true' : 'false';
        } else {
          params[paramName] = el.value;
        }
      }
      vscode.postMessage({ command: 'submit', parameters: params });
    });
    document.getElementById('cancelBtn').addEventListener('click', () => {
      vscode.postMessage({ command: 'cancel' });
    });
    // Focus first input
    const first = form.querySelector('input');
    if (first) first.focus();
  </script>
</body>
</html>`;
  });
}

function logOutput(message: string, isError = false): void {
  outputChannel.appendLine(message);
  outputChannel.show(true);
  if (isError) { vscode.window.showErrorMessage(message); }
}

function formatJsonResult(obj: unknown, indent = 0): string {
  if (obj === null || obj === undefined) { return ''; }
  if (typeof obj === 'string' || typeof obj === 'number' || typeof obj === 'boolean') {
    return String(obj);
  }
  if (Array.isArray(obj)) {
    return obj.map(item => formatJsonResult(item, indent)).join('\n');
  }
  if (typeof obj === 'object') {
    const prefix = '  '.repeat(indent);
    return Object.entries(obj as Record<string, unknown>)
      .filter(([, v]) => v !== null && v !== undefined)
      .map(([k, v]) => {
        const label = k.charAt(0).toUpperCase() + k.slice(1).replace(/([A-Z])/g, ' $1');
        if (typeof v === 'object') {
          return `${prefix}${label}:\n${formatJsonResult(v, indent + 1)}`;
        }
        return `${prefix}${label}: ${v}`;
      })
      .join('\n');
  }
  return String(obj);
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

  // Send the client's timezone so the server can resolve time-of-day autostop values
  parameters['_timezone'] = Intl.DateTimeFormat().resolvedOptions().timeZone;

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
            const result = await response.json();
            logOutput(`[${definition.name}] ${formatJsonResult(result)}`);
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
          const result = await response.json();
          logOutput(`[${functionName}] ${formatJsonResult(result)}`);
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
          const result = await response.json() as { logs: string };
          const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
          const uri = vscode.Uri.parse(`fkh-log:${containerName}-${timestamp}.log`);
          containerLogContents.set(uri.toString(), result.logs ?? '');
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
