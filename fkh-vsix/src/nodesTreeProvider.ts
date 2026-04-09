import * as vscode from 'vscode';
import { getProjects } from './readALGoSettings';

// ── AL-Go Projects tree ──────────────────────────────────────────────────────

export class ProjectTreeItem extends vscode.TreeItem {
  constructor(
    public readonly label: string,
    public readonly collapsibleState: vscode.TreeItemCollapsibleState,
    public readonly projectName?: string,
  ) {
    super(label, collapsibleState);
  }
}

export class ProjectsTreeProvider implements vscode.TreeDataProvider<ProjectTreeItem> {
  private _onDidChangeTreeData = new vscode.EventEmitter<ProjectTreeItem | undefined>();
  readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

  private projects: string[] = [];
  private initialized = false;

  async refresh(): Promise<void> {
    this.projects = await getProjects();
    this.initialized = true;
    this._onDidChangeTreeData.fire(undefined);
  }

  getTreeItem(element: ProjectTreeItem): vscode.TreeItem {
    return element;
  }

  getChildren(element?: ProjectTreeItem): ProjectTreeItem[] {
    if (element) { return []; }
    if (!this.initialized) { return []; }

    if (this.projects.length === 0) {
      const empty = new ProjectTreeItem('No AL-Go projects found', vscode.TreeItemCollapsibleState.None);
      empty.iconPath = new vscode.ThemeIcon('info');
      return [empty];
    }

    return this.projects.map(name => {
      const item = new ProjectTreeItem(name, vscode.TreeItemCollapsibleState.None, name);
      item.iconPath = new vscode.ThemeIcon('symbol-folder');
      item.contextValue = 'algoProject';
      return item;
    });
  }
}

// ── Containers tree ──────────────────────────────────────────────────────────

export interface NodeInfo {
  appLabel: string;
  name: string;
  status: string;
  properties: { label: string; value: string }[];
}

export class ContainerTreeItem extends vscode.TreeItem {
  constructor(
    public readonly label: string,
    public readonly collapsibleState: vscode.TreeItemCollapsibleState,
    public readonly nodeInfo?: NodeInfo,
  ) {
    super(label, collapsibleState);
  }
}

export class ContainersTreeProvider implements vscode.TreeDataProvider<ContainerTreeItem> {
  private _onDidChangeTreeData = new vscode.EventEmitter<ContainerTreeItem | undefined>();
  readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

  private nodes: NodeInfo[] = [];
  private initialized = false;
  private _getBaseUrl: () => string | undefined;
  private _getGitHubSession: () => Promise<vscode.AuthenticationSession | undefined>;

  constructor(
    getBaseUrl: () => string | undefined,
    getGitHubSession: () => Promise<vscode.AuthenticationSession | undefined>
  ) {
    this._getBaseUrl = getBaseUrl;
    this._getGitHubSession = getGitHubSession;
  }

  async refresh(): Promise<void> {
    this.nodes = await this.fetchNodes();
    this.initialized = true;
    this._onDidChangeTreeData.fire(undefined);
  }

  getTreeItem(element: ContainerTreeItem): vscode.TreeItem {
    return element;
  }

  getChildren(element?: ContainerTreeItem): ContainerTreeItem[] {
    if (!element) {
      if (!this.initialized) { return []; }
      if (this.nodes.length === 0) {
        const empty = new ContainerTreeItem('No containers', vscode.TreeItemCollapsibleState.None);
        empty.iconPath = new vscode.ThemeIcon('info');
        return [empty];
      }
      return this.nodes.map(node => {
        const statusLower = node.status.toLowerCase();
        const icon = statusLower.startsWith('running')
          ? new vscode.ThemeIcon('vm-running', new vscode.ThemeColor('testing.iconPassed'))
          : statusLower.startsWith('starting')
            ? new vscode.ThemeIcon('sync~spin', new vscode.ThemeColor('testing.iconQueued'))
            : new vscode.ThemeIcon('vm', new vscode.ThemeColor('testing.iconSkipped'));

        const item = new ContainerTreeItem(
          `${node.name} (${node.status})`,
          vscode.TreeItemCollapsibleState.Collapsed,
          node
        );
        item.iconPath = icon;
        item.tooltip = `${node.appLabel}\nStatus: ${node.status}`;
        item.contextValue = `node-${node.status.toLowerCase()}`;
        return item;
      });
    }

    if (element.nodeInfo) {
      return element.nodeInfo.properties.map(prop => {
        const child = new ContainerTreeItem(
          `${prop.label}: ${prop.value}`,
          vscode.TreeItemCollapsibleState.None
        );
        child.tooltip = `${prop.label}: ${prop.value}`;

        if (prop.label === 'WebClient') {
          child.command = {
            command: 'vscode.open',
            title: 'Open WebClient',
            arguments: [vscode.Uri.parse(prop.value)],
          };
          child.iconPath = new vscode.ThemeIcon('link-external');
        } else if (prop.label === 'Image') {
          child.iconPath = new vscode.ThemeIcon('package');
        } else if (prop.label === 'CPU') {
          child.iconPath = new vscode.ThemeIcon('dashboard');
        } else if (prop.label === 'Memory') {
          child.iconPath = new vscode.ThemeIcon('server-process');
        } else if (prop.label === 'AutoStop') {
          child.iconPath = new vscode.ThemeIcon('watch');
        } else if (prop.label === 'Status') {
          child.iconPath = new vscode.ThemeIcon('info');
        }

        return child;
      });
    }

    return [];
  }

  private async fetchNodes(): Promise<NodeInfo[]> {
    const baseUrl = this._getBaseUrl();
    if (!baseUrl) { return []; }

    const session = await this._getGitHubSession();
    if (!session) { return []; }

    try {
      const response = await fetch(`${baseUrl}/ListNodes`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${session.accessToken}`,
        },
        body: JSON.stringify({ parameters: {} }),
      });

      if (!response.ok) { return []; }

      const result = await response.json() as { message: string };
      return this.parseNodesMessage(result.message);
    } catch {
      return [];
    }
  }

  private parseNodesMessage(message: string): NodeInfo[] {
    const nodes: NodeInfo[] = [];
    const lines = message.split('\n');

    let current: NodeInfo | undefined;

    for (const line of lines) {
      const headerMatch = line.match(/^  (\S+)\s*$/);
      if (headerMatch) {
        if (current) { nodes.push(current); }
        current = {
          appLabel: headerMatch[1],
          name: headerMatch[1],
          status: 'Unknown',
          properties: [],
        };
        continue;
      }

      if (!current) { continue; }

      const propMatch = line.match(/^    (\S+?)\s*:\s*(.+)$/);
      if (propMatch) {
        const key = propMatch[1];
        const value = propMatch[2].trim();

        if (key === 'Name') {
          current.name = value;
        } else if (key === 'Status') {
          const statusWord = value.split(' ')[0];
          current.status = statusWord;
          current.properties.push({ label: 'Status', value });
        } else {
          current.properties.push({ label: key, value });
        }
      }
    }

    if (current) { nodes.push(current); }
    return nodes;
  }
}

// ── Images tree ──────────────────────────────────────────────────────────────

export interface ImageInfo {
  repository: string;
  tags: { name: string; size: string; updated: string; lastPull: string }[];
}

export class ImageTreeItem extends vscode.TreeItem {
  constructor(
    public readonly label: string,
    public readonly collapsibleState: vscode.TreeItemCollapsibleState,
    public readonly imageInfo?: ImageInfo,
  ) {
    super(label, collapsibleState);
  }
}

export class ImagesTreeProvider implements vscode.TreeDataProvider<ImageTreeItem> {
  private _onDidChangeTreeData = new vscode.EventEmitter<ImageTreeItem | undefined>();
  readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

  private images: ImageInfo[] = [];
  private initialized = false;
  private _getBaseUrl: () => string | undefined;
  private _getGitHubSession: () => Promise<vscode.AuthenticationSession | undefined>;

  constructor(
    getBaseUrl: () => string | undefined,
    getGitHubSession: () => Promise<vscode.AuthenticationSession | undefined>
  ) {
    this._getBaseUrl = getBaseUrl;
    this._getGitHubSession = getGitHubSession;
  }

  async refresh(): Promise<void> {
    this.images = await this.fetchImages();
    this.initialized = true;
    this._onDidChangeTreeData.fire(undefined);
  }

  getTreeItem(element: ImageTreeItem): vscode.TreeItem {
    return element;
  }

  getChildren(element?: ImageTreeItem): ImageTreeItem[] {
    if (!element) {
      if (!this.initialized) { return []; }
      if (this.images.length === 0) {
        const empty = new ImageTreeItem('No images found', vscode.TreeItemCollapsibleState.None);
        empty.iconPath = new vscode.ThemeIcon('info');
        return [empty];
      }
      return this.images.map(img => {
        const item = new ImageTreeItem(
          img.repository,
          vscode.TreeItemCollapsibleState.Collapsed,
          img
        );
        item.iconPath = new vscode.ThemeIcon('package');
        item.tooltip = `${img.repository} (${img.tags.length} tag${img.tags.length !== 1 ? 's' : ''})`;
        item.contextValue = 'acrRepository';
        return item;
      });
    }

    if (element.imageInfo) {
      return element.imageInfo.tags.map(tag => {
        const child = new ImageTreeItem(
          `${tag.name}  (${tag.size}, ${tag.updated})`,
          vscode.TreeItemCollapsibleState.None
        );
        child.iconPath = new vscode.ThemeIcon('tag');
        child.tooltip = `${tag.name}\nSize: ${tag.size}\nUpdated: ${tag.updated}\nLast pulled: ${tag.lastPull}`;
        child.description = tag.lastPull !== 'never' ? `pulled: ${tag.lastPull}` : undefined;
        child.contextValue = 'acrTag';
        return child;
      });
    }

    return [];
  }

  private async fetchImages(): Promise<ImageInfo[]> {
    const baseUrl = this._getBaseUrl();
    if (!baseUrl) { return []; }

    const session = await this._getGitHubSession();
    if (!session) { return []; }

    try {
      const response = await fetch(`${baseUrl}/ListImages`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${session.accessToken}`,
        },
        body: JSON.stringify({ parameters: {} }),
      });

      if (!response.ok) { return []; }

      const result = await response.json() as { message: string };
      return this.parseImagesMessage(result.message);
    } catch {
      return [];
    }
  }

  private parseImagesMessage(message: string): ImageInfo[] {
    const images: ImageInfo[] = [];
    let current: ImageInfo | undefined;

    const lines = message.split('\n');
    for (const line of lines) {
      const repoMatch = line.match(/^\s*Repository:\s*(.+)$/);
      if (repoMatch) {
        if (current) { images.push(current); }
        current = { repository: repoMatch[1].trim(), tags: [] };
        continue;
      }

      if (!current) { continue; }

      // Skip the "Tags:" line
      if (line.match(/^\s*Tags:/)) { continue; }

      // Parse tag lines: "    tagname  (size, date, pulled: date)"
      const tagMatch = line.match(/^\s{4}(\S+)\s+\(([^,]+),\s*([^,]+),\s*pulled:\s*(.+)\)$/);
      if (tagMatch) {
        current.tags.push({
          name: tagMatch[1],
          size: tagMatch[2].trim(),
          updated: tagMatch[3].trim(),
          lastPull: tagMatch[4].trim(),
        });
      }
    }

    if (current) { images.push(current); }
    return images;
  }
}
