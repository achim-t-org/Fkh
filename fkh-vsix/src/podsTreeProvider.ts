import * as vscode from 'vscode';
import { getProjects } from './readALGoSettings';

// ── AL-Go Projects tree ──────────────────────────────────────────────────────

export class ProjectTreeItem extends vscode.TreeItem {
  constructor(
    public readonly label: string,
    public readonly collapsibleState: vscode.TreeItemCollapsibleState,
    public readonly projectName?: string,
    public readonly podInfo?: PodInfo,
    public readonly isProperty?: boolean,
  ) {
    super(label, collapsibleState);
  }
}

export class ProjectsTreeProvider implements vscode.TreeDataProvider<ProjectTreeItem> {
  private _onDidChangeTreeData = new vscode.EventEmitter<ProjectTreeItem | undefined>();
  readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

  private projects: string[] = [];
  private initialized = false;
  private _getRepoName: () => string;
  private _getPods: () => PodInfo[];

  constructor(getRepoName: () => string, getPods: () => PodInfo[]) {
    this._getRepoName = getRepoName;
    this._getPods = getPods;
  }

  async refresh(): Promise<void> {
    this.projects = await getProjects();
    this.initialized = true;
    this._onDidChangeTreeData.fire(undefined);
  }

  getTreeItem(element: ProjectTreeItem): vscode.TreeItem {
    return element;
  }

  getChildren(element?: ProjectTreeItem): ProjectTreeItem[] {
    if (!element) {
      if (!this.initialized) { return []; }

      if (this.projects.length === 0) {
        const empty = new ProjectTreeItem('No AL-Go projects found', vscode.TreeItemCollapsibleState.None);
        empty.iconPath = new vscode.ThemeIcon('info');
        return [empty];
      }

      return this.projects.map(name => {
        const pods = this.getPodsForProject(name);
        const state = pods.length > 0
          ? vscode.TreeItemCollapsibleState.Expanded
          : vscode.TreeItemCollapsibleState.None;
        const item = new ProjectTreeItem(name, state, name);
        item.iconPath = new vscode.ThemeIcon('symbol-folder');
        item.contextValue = 'algoProject';
        if (pods.length > 0) {
          item.description = `${pods.length} pod${pods.length !== 1 ? 's' : ''}`;
        }
        return item;
      });
    }

    // Children of a project: pods
    if (element.projectName && !element.podInfo) {
      const pods = this.getPodsForProject(element.projectName);
      return pods.map(pod => {
        const statusLower = pod.status.toLowerCase();
        const icon = statusLower.startsWith('running')
          ? new vscode.ThemeIcon('vm-running', new vscode.ThemeColor('testing.iconPassed'))
          : statusLower.startsWith('starting')
            ? new vscode.ThemeIcon('sync~spin', new vscode.ThemeColor('testing.iconQueued'))
            : new vscode.ThemeIcon('vm', new vscode.ThemeColor('testing.iconSkipped'));

        const item = new ProjectTreeItem(
          `${pod.name} (${pod.status})`,
          vscode.TreeItemCollapsibleState.Collapsed,
          undefined,
          pod
        );
        item.iconPath = icon;
        item.tooltip = `${pod.appLabel}\nStatus: ${pod.status}`;
        item.contextValue = `pod-${pod.status.toLowerCase()}`;
        return item;
      });
    }

    // Children of a pod: properties
    if (element.podInfo) {
      return element.podInfo.properties.map(prop => {
        const child = new ProjectTreeItem(
          `${prop.label}: ${prop.value}`,
          vscode.TreeItemCollapsibleState.None,
          undefined,
          undefined,
          true
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
        } else if (prop.label === 'Memory') {
          child.iconPath = new vscode.ThemeIcon('server-process');
        } else if (prop.label === 'AutoStop') {
          child.iconPath = new vscode.ThemeIcon('watch');
        } else if (prop.label === 'Repo') {
          child.iconPath = new vscode.ThemeIcon('repo');
        } else if (prop.label === 'Project') {
          child.iconPath = new vscode.ThemeIcon('symbol-folder');
        } else if (prop.label === 'Status') {
          child.iconPath = new vscode.ThemeIcon('info');
        }

        return child;
      });
    }

    return [];
  }

  private getPodsForProject(projectName: string): PodInfo[] {
    const repoName = this._getRepoName();
    if (!repoName) { return []; }
    const pods = this._getPods();
    return pods.filter(pod => {
      const podRepo = pod.properties.find(p => p.label === 'Repo')?.value ?? '';
      const podProject = pod.properties.find(p => p.label === 'Project')?.value ?? '';
      return podRepo.toLowerCase() === repoName.toLowerCase()
        && podProject.toLowerCase() === projectName.toLowerCase();
    });
  }
}

// ── Pods tree ──────────────────────────────────────────────────────────

export interface PodInfo {
  appLabel: string;
  name: string;
  status: string;
  properties: { label: string; value: string }[];
}

export class PodTreeItem extends vscode.TreeItem {
  constructor(
    public readonly label: string,
    public readonly collapsibleState: vscode.TreeItemCollapsibleState,
    public readonly podInfo?: PodInfo,
  ) {
    super(label, collapsibleState);
  }
}

export class PodsTreeProvider implements vscode.TreeDataProvider<PodTreeItem> {
  private _onDidChangeTreeData = new vscode.EventEmitter<PodTreeItem | undefined>();
  readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

  private pods: PodInfo[] = [];
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

  getPods(): PodInfo[] {
    return this.pods;
  }

  async refresh(): Promise<void> {
    this.pods = await this.fetchPods();
    this.initialized = true;
    this._onDidChangeTreeData.fire(undefined);
  }

  getTreeItem(element: PodTreeItem): vscode.TreeItem {
    return element;
  }

  getChildren(element?: PodTreeItem): PodTreeItem[] {
    if (!element) {
      if (!this.initialized) { return []; }
      if (this.pods.length === 0) {
        const empty = new PodTreeItem('No pods', vscode.TreeItemCollapsibleState.None);
        empty.iconPath = new vscode.ThemeIcon('info');
        return [empty];
      }
      return this.pods.map(pod => {
        const statusLower = pod.status.toLowerCase();
        const icon = statusLower.startsWith('running')
          ? new vscode.ThemeIcon('vm-running', new vscode.ThemeColor('testing.iconPassed'))
          : statusLower.startsWith('starting')
            ? new vscode.ThemeIcon('sync~spin', new vscode.ThemeColor('testing.iconQueued'))
            : new vscode.ThemeIcon('vm', new vscode.ThemeColor('testing.iconSkipped'));

        const item = new PodTreeItem(
          `${pod.name} (${pod.status})`,
          vscode.TreeItemCollapsibleState.Collapsed,
          pod
        );
        item.iconPath = icon;
        item.tooltip = `${pod.appLabel}\nStatus: ${pod.status}`;
        item.contextValue = `pod-${pod.status.toLowerCase()}`;
        return item;
      });
    }

    if (element.podInfo) {
      return element.podInfo.properties.map(prop => {
        const child = new PodTreeItem(
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
        } else if (prop.label === 'Memory') {
          child.iconPath = new vscode.ThemeIcon('server-process');
        } else if (prop.label === 'AutoStop') {
          child.iconPath = new vscode.ThemeIcon('watch');
        } else if (prop.label === 'Repo') {
          child.iconPath = new vscode.ThemeIcon('repo');
        } else if (prop.label === 'Project') {
          child.iconPath = new vscode.ThemeIcon('symbol-folder');
        } else if (prop.label === 'Status') {
          child.iconPath = new vscode.ThemeIcon('info');
        }

        return child;
      });
    }

    return [];
  }

  private async fetchPods(): Promise<PodInfo[]> {
    const baseUrl = this._getBaseUrl();
    if (!baseUrl) { return []; }

    const session = await this._getGitHubSession();
    if (!session) { return []; }

    try {
      const response = await fetch(`${baseUrl}/ListPods`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${session.accessToken}`,
        },
        body: JSON.stringify({ parameters: {} }),
      });

      if (!response.ok) { return []; }

      const result = await response.json() as { message: string };
      return this.parsePodsMessage(result.message);
    } catch {
      return [];
    }
  }

  private parsePodsMessage(message: string): PodInfo[] {
    const pods: PodInfo[] = [];
    const lines = message.split(/\r?\n/);

    let current: PodInfo | undefined;

    for (const line of lines) {
      const headerMatch = line.match(/^  (\S+)\s*$/);
      if (headerMatch) {
        if (current) { pods.push(current); }
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

    if (current) { pods.push(current); }
    return pods;
  }
}

// ── Images tree ──────────────────────────────────────────────────────────────

export interface ImageInfo {
  repository: string;
  tags: { name: string; size: string; updated: string; lastUsed: string }[];
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
        child.tooltip = `${tag.name}\nSize: ${tag.size}\nUpdated: ${tag.updated}\nLast used: ${tag.lastUsed}`;
        child.description = tag.lastUsed !== 'never' ? `last used: ${tag.lastUsed}` : undefined;
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

      // Parse tag lines: "    tagname  (size, date, last used: date)"
      const tagMatch = line.match(/^\s{4}(\S+)\s+\(([^,]+),\s*([^,]+),\s*last used:\s*(.+)\)$/);
      if (tagMatch) {
        current.tags.push({
          name: tagMatch[1],
          size: tagMatch[2].trim(),
          updated: tagMatch[3].trim(),
          lastUsed: tagMatch[4].trim(),
        });
      }
    }

    if (current) { images.push(current); }
    return images;
  }
}

// ── Nodes tree (admin only) ──────────────────────────────────────────────────

export interface NodeInfo {
  name: string;
  status: string;
  podAppLabels: string[];
  properties: { label: string; value: string }[];
}

export class NodeTreeItem extends vscode.TreeItem {
  constructor(
    public readonly label: string,
    public readonly collapsibleState: vscode.TreeItemCollapsibleState,
    public readonly nodeInfo?: NodeInfo,
    public readonly podInfo?: PodInfo,
  ) {
    super(label, collapsibleState);
  }
}

export class NodesTreeProvider implements vscode.TreeDataProvider<NodeTreeItem> {
  private _onDidChangeTreeData = new vscode.EventEmitter<NodeTreeItem | undefined>();
  readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

  private nodes: NodeInfo[] = [];
  private initialized = false;
  private _visible = false;
  private _getBaseUrl: () => string | undefined;
  private _getGitHubSession: () => Promise<vscode.AuthenticationSession | undefined>;
  private _getPods: () => PodInfo[];

  constructor(
    getBaseUrl: () => string | undefined,
    getGitHubSession: () => Promise<vscode.AuthenticationSession | undefined>,
    getPods: () => PodInfo[]
  ) {
    this._getBaseUrl = getBaseUrl;
    this._getGitHubSession = getGitHubSession;
    this._getPods = getPods;
  }

  get visible(): boolean { return this._visible; }

  async refresh(): Promise<void> {
    const result = await this.fetchNodes();
    this.nodes = result.nodes;
    this._visible = result.visible;
    this.initialized = true;
    this._onDidChangeTreeData.fire(undefined);
  }

  getTreeItem(element: NodeTreeItem): vscode.TreeItem {
    return element;
  }

  getChildren(element?: NodeTreeItem): NodeTreeItem[] {
    if (!element) {
      if (!this.initialized) { return []; }
      if (this.nodes.length === 0) {
        const empty = new NodeTreeItem('No Windows nodes found', vscode.TreeItemCollapsibleState.None);
        empty.iconPath = new vscode.ThemeIcon('info');
        return [empty];
      }
      return this.nodes.map(node => {
        const statusLower = node.status.toLowerCase();
        const icon = statusLower === 'ready'
          ? new vscode.ThemeIcon('vm-running', new vscode.ThemeColor('testing.iconPassed'))
          : new vscode.ThemeIcon('vm', new vscode.ThemeColor('testing.iconFailed'));

        const item = new NodeTreeItem(
          `${node.name} (${node.status})`,
          vscode.TreeItemCollapsibleState.Collapsed,
          node
        );
        item.iconPath = icon;
        item.tooltip = `${node.name}\nStatus: ${node.status}`;
        item.contextValue = 'windowsNode';
        if (node.podAppLabels.length > 0) {
          item.description = `${node.podAppLabels.length} pod${node.podAppLabels.length !== 1 ? 's' : ''}`;
        }
        return item;
      });
    }

    // Children of a node: properties + pods
    if (element.nodeInfo && !element.podInfo) {
      const children: NodeTreeItem[] = [];

      // Node properties
      for (const prop of element.nodeInfo.properties) {
        const child = new NodeTreeItem(
          `${prop.label}: ${prop.value}`,
          vscode.TreeItemCollapsibleState.None
        );
        child.tooltip = `${prop.label}: ${prop.value}`;

        if (prop.label === 'CNS') {
          child.iconPath = new vscode.ThemeIcon(
            prop.value === 'Ready' ? 'pass' : 'error',
            prop.value === 'Ready'
              ? new vscode.ThemeColor('testing.iconPassed')
              : new vscode.ThemeColor('testing.iconFailed')
          );
        } else if (prop.label === 'CPU') {
          child.iconPath = new vscode.ThemeIcon('dashboard');
        } else if (prop.label === 'Memory') {
          child.iconPath = new vscode.ThemeIcon('server-process');
        } else if (prop.label === 'Kubelet') {
          child.iconPath = new vscode.ThemeIcon('versions');
        } else if (prop.label === 'OS') {
          child.iconPath = new vscode.ThemeIcon('device-desktop');
        }

        children.push(child);
      }

      // Pods on this node
      const allPods = this._getPods();
      const nodePodLabels = new Set(element.nodeInfo.podAppLabels);
      const nodePods = allPods.filter(p => nodePodLabels.has(p.appLabel));
      for (const pod of nodePods) {
        const statusLower = pod.status.toLowerCase();
        const podIcon = statusLower.startsWith('running')
          ? new vscode.ThemeIcon('vm-running', new vscode.ThemeColor('testing.iconPassed'))
          : statusLower.startsWith('starting')
            ? new vscode.ThemeIcon('sync~spin', new vscode.ThemeColor('testing.iconQueued'))
            : new vscode.ThemeIcon('vm', new vscode.ThemeColor('testing.iconSkipped'));

        const podItem = new NodeTreeItem(
          `${pod.name} (${pod.status})`,
          vscode.TreeItemCollapsibleState.Collapsed,
          undefined,
          pod
        );
        podItem.iconPath = podIcon;
        podItem.tooltip = `${pod.appLabel}\nStatus: ${pod.status}`;
        podItem.contextValue = `pod-${pod.status.toLowerCase()}`;
        children.push(podItem);
      }

      return children;
    }

    // Children of a pod under a node: properties
    if (element.podInfo) {
      return element.podInfo.properties.map(prop => {
        const child = new NodeTreeItem(
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
        } else if (prop.label === 'Memory') {
          child.iconPath = new vscode.ThemeIcon('server-process');
        } else if (prop.label === 'AutoStop') {
          child.iconPath = new vscode.ThemeIcon('watch');
        } else if (prop.label === 'Repo') {
          child.iconPath = new vscode.ThemeIcon('repo');
        } else if (prop.label === 'Project') {
          child.iconPath = new vscode.ThemeIcon('symbol-folder');
        } else if (prop.label === 'Status') {
          child.iconPath = new vscode.ThemeIcon('info');
        }

        return child;
      });
    }

    return [];
  }

  private async fetchNodes(): Promise<{ nodes: NodeInfo[]; visible: boolean }> {
    const baseUrl = this._getBaseUrl();
    if (!baseUrl) { return { nodes: [], visible: false }; }

    const session = await this._getGitHubSession();
    if (!session) { return { nodes: [], visible: false }; }

    try {
      const response = await fetch(`${baseUrl}/ListNodes`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${session.accessToken}`,
        },
        body: JSON.stringify({ parameters: {} }),
      });

      if (response.status === 403 || response.status === 500) {
        // Not admin — hide the view
        return { nodes: [], visible: false };
      }

      if (!response.ok) { return { nodes: [], visible: false }; }

      const result = await response.json() as { message: string };
      return { nodes: this.parseNodesMessage(result.message), visible: true };
    } catch {
      return { nodes: [], visible: false };
    }
  }

  private parseNodesMessage(message: string): NodeInfo[] {
    const nodes: NodeInfo[] = [];
    const lines = message.split(/\r?\n/);

    let current: NodeInfo | undefined;

    for (const line of lines) {
      const headerMatch = line.match(/^  (\S+)\s*$/);
      if (headerMatch) {
        if (current) { nodes.push(current); }
        current = {
          name: headerMatch[1],
          status: 'Unknown',
          podAppLabels: [],
          properties: [],
        };
        continue;
      }

      if (!current) { continue; }

      const propMatch = line.match(/^    (\S+?):\s*(.+)$/);
      if (propMatch) {
        const key = propMatch[1];
        const value = propMatch[2].trim();

        if (key === 'Status') {
          current.status = value;
        } else if (key === 'Pods') {
          current.podAppLabels = value === '0' ? [] : value.split(',').map(s => s.trim());
          continue;
        }
        current.properties.push({ label: key, value });
      }
    }

    if (current) { nodes.push(current); }
    return nodes;
  }
}
