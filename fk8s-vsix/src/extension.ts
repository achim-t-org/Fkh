import * as vscode from 'vscode';

let functionCatalog: FunctionCatalogResponse | undefined;

function getBaseUrl(): string | undefined {
  const url = vscode.workspace.getConfiguration('fk8s').get<string>('baseUrl', '').trim();
  if (!url) {
    vscode.window.showErrorMessage(
      'FK8s: Base URL is not configured. Set "fk8s.baseUrl" in your settings.',
      'Open Settings'
    ).then((action: string | undefined) => {
      if (action === 'Open Settings') {
        vscode.commands.executeCommand('workbench.action.openSettings', 'fk8s.baseUrl');
      }
    });
    return undefined;
  }
  return url;
}

export function activate(context: vscode.ExtensionContext) {
  context.subscriptions.push(
    vscode.commands.registerCommand('fk8s.run', async () => {
      const catalog = await getFunctionCatalog();
      if (!catalog) { return; }

      const items = catalog.functions.map(f => ({
        label: `FK8s: ${f.name}`,
        description: f.description,
        functionName: f.name,
      }));

      const picked = await vscode.window.showQuickPick(items, {
        placeHolder: 'Select a command to run',
      });
      if (!picked) { return; }

      await invokeFunctionByName(picked.functionName);
    }),
    vscode.commands.registerCommand('fk8s.createContainer', async () => {
      const artifactUrl = 'https://example.com';
      await invokeFunctionByName('CreateNode', { artifactUrl });
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

async function getGitHubSession(): Promise<vscode.AuthenticationSession | undefined> {
  try {
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

  for (const param of definition.parameters) {
    const prefilledKey = Object.keys(prefilled).find(
      k => k.toLowerCase() === param.name.toLowerCase()
    );
    if (prefilledKey) {
      continue;
    }

    let value: string | undefined = undefined;

    while (true) {
      value = await vscode.window.showInputBox({
        prompt: `${param.name}: ${param.description}`,
        placeHolder: param.defaultValue ?? '',
        value: param.defaultValue ?? '',
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
      cancellable: false,
    },
    async () => {
      try {
        const body: FunctionInvokeRequest = { parameters };

        const response = await fetch(`${getBaseUrl()}/${definition.route}`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            Authorization: `Bearer ${session.accessToken}`,
          },
          body: JSON.stringify(body),
        });

        if (response.ok) {
          const result = await response.json() as { message: string };
          vscode.window.showInformationMessage(`✅ ${result.message}`);
        } else if (response.status === 401 || response.status === 403) {
          vscode.window.showErrorMessage(
            'Access denied. Make sure your GitHub account is a member of an authorized team.'
          );
        } else {
          const error = await response.text();
          vscode.window.showErrorMessage(`Failed to ${definition.name}: ${error}`);
        }
      } catch (err) {
        vscode.window.showErrorMessage(
          `Could not reach the provisioning service: ${err instanceof Error ? err.message : String(err)}`
        );
      }
    }
  );
}

export function deactivate() {}
