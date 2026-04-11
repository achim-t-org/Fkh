import * as vscode from 'vscode';
import { getProjects } from './readALGoSettings';

// ── AL-Go Projects tree ──────────────────────────────────────────────────────

export class ProjectTreeItem extends vscode.TreeItem {
  constructor(
    public readonly label: string,
    public readonly collapsibleState: vscode.TreeItemCollapsibleState,
    public readonly projectName?: string,
    public readonly containerInfo?: ContainerInfo,
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
  private _getContainers: () => ContainerInfo[];

  constructor(getRepoName: () => string, getContainers: () => ContainerInfo[]) {
    this._getRepoName = getRepoName;
    this._getContainers = getContainers;
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
        const containers = this.getContainersForProject(name);
        const state = containers.length > 0
          ? vscode.TreeItemCollapsibleState.Expanded
          : vscode.TreeItemCollapsibleState.None;
        const item = new ProjectTreeItem(name, state, name);
        item.iconPath = new vscode.ThemeIcon('symbol-folder');
        item.contextValue = 'algoProject';
        if (containers.length > 0) {
          item.description = `${containers.length} container${containers.length !== 1 ? 's' : ''}`;
        }
        return item;
      });
    }

    // Children of a project: containers
    if (element.projectName && !element.containerInfo) {
      const containers = this.getContainersForProject(element.projectName);
      return containers.map(container => {
        const statusLower = container.status.toLowerCase();
        const icon = statusLower.startsWith('running')
          ? new vscode.ThemeIcon('vm-running', new vscode.ThemeColor('testing.iconPassed'))
          : statusLower.startsWith('starting')
            ? new vscode.ThemeIcon('sync~spin', new vscode.ThemeColor('testing.iconQueued'))
            : new vscode.ThemeIcon('vm', new vscode.ThemeColor('testing.iconSkipped'));

        const item = new ProjectTreeItem(
          `${container.name} (${container.status})`,
          vscode.TreeItemCollapsibleState.Collapsed,
          undefined,
          container
        );
        item.iconPath = icon;
        item.tooltip = `${container.appLabel}\nStatus: ${container.status}`;
        item.contextValue = `container-${container.status.toLowerCase()}`;
        return item;
      });
    }

    // Children of a container: properties
    if (element.containerInfo) {
      return element.containerInfo.properties.map(prop => {
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

  private getContainersForProject(projectName: string): ContainerInfo[] {
    const repoName = this._getRepoName();
    if (!repoName) { return []; }
    const containers = this._getContainers();
    return containers.filter(container => {
      const containerRepo = container.properties.find(p => p.label === 'Repo')?.value ?? '';
      const containerProject = container.properties.find(p => p.label === 'Project')?.value ?? '';
      return containerRepo.toLowerCase() === repoName.toLowerCase()
        && containerProject.toLowerCase() === projectName.toLowerCase();
    });
  }
}

// ── Containers tree ──────────────────────────────────────────────────────────

export interface ContainerInfo {
  appLabel: string;
  name: string;
  status: string;
  properties: { label: string; value: string }[];
}

export class ContainerTreeItem extends vscode.TreeItem {
  constructor(
    public readonly label: string,
    public readonly collapsibleState: vscode.TreeItemCollapsibleState,
    public readonly containerInfo?: ContainerInfo,
  ) {
    super(label, collapsibleState);
  }
}

export class ContainersTreeProvider implements vscode.TreeDataProvider<ContainerTreeItem> {
  private _onDidChangeTreeData = new vscode.EventEmitter<ContainerTreeItem | undefined>();
  readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

  private containers: ContainerInfo[] = [];
  private initialized = false;
  private _getBackendUrl: () => string | undefined;
  private _getGitHubSession: () => Promise<vscode.AuthenticationSession | undefined>;

  constructor(
    getBackendUrl: () => string | undefined,
    getGitHubSession: () => Promise<vscode.AuthenticationSession | undefined>
  ) {
    this._getBackendUrl = getBackendUrl;
    this._getGitHubSession = getGitHubSession;
  }

  getContainers(): ContainerInfo[] {
    return this.containers;
  }

  async refresh(): Promise<void> {
    this.containers = await this.fetchContainers();
    this.initialized = true;
    this._onDidChangeTreeData.fire(undefined);
  }

  getTreeItem(element: ContainerTreeItem): vscode.TreeItem {
    return element;
  }

  getChildren(element?: ContainerTreeItem): ContainerTreeItem[] {
    if (!element) {
      if (!this.initialized) { return []; }
      if (this.containers.length === 0) {
        const empty = new ContainerTreeItem('No containers', vscode.TreeItemCollapsibleState.None);
        empty.iconPath = new vscode.ThemeIcon('info');
        return [empty];
      }
      return this.containers.map(container => {
        const statusLower = container.status.toLowerCase();
        const icon = statusLower.startsWith('running')
          ? new vscode.ThemeIcon('vm-running', new vscode.ThemeColor('testing.iconPassed'))
          : statusLower.startsWith('starting')
            ? new vscode.ThemeIcon('sync~spin', new vscode.ThemeColor('testing.iconQueued'))
            : new vscode.ThemeIcon('vm', new vscode.ThemeColor('testing.iconSkipped'));

        const item = new ContainerTreeItem(
          `${container.name} (${container.status})`,
          vscode.TreeItemCollapsibleState.Collapsed,
          container
        );
        item.iconPath = icon;
        item.tooltip = `${container.appLabel}\nStatus: ${container.status}`;
        item.contextValue = `container-${container.status.toLowerCase()}`;
        return item;
      });
    }

    if (element.containerInfo) {
      return element.containerInfo.properties.map(prop => {
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

  private async fetchContainers(): Promise<ContainerInfo[]> {
    const baseUrl = this._getBackendUrl();
    if (!baseUrl) { return []; }

    const session = await this._getGitHubSession();
    if (!session) { return []; }

    try {
      const response = await fetch(`${baseUrl}/ListContainers`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${session.accessToken}`,
        },
        body: JSON.stringify({ parameters: {} }),
      });

      if (!response.ok) { return []; }

      const result = await response.json() as { message: string };
      return this.parseContainersMessage(result.message);
    } catch {
      return [];
    }
  }

  private parseContainersMessage(message: string): ContainerInfo[] {
    const containers: ContainerInfo[] = [];
    const lines = message.split(/\r?\n/);

    let current: ContainerInfo | undefined;

    for (const line of lines) {
      const headerMatch = line.match(/^  (\S+)\s*$/);
      if (headerMatch) {
        if (current) { containers.push(current); }
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

    if (current) { containers.push(current); }
    return containers;
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
  private _getBackendUrl: () => string | undefined;
  private _getGitHubSession: () => Promise<vscode.AuthenticationSession | undefined>;

  constructor(
    getBackendUrl: () => string | undefined,
    getGitHubSession: () => Promise<vscode.AuthenticationSession | undefined>
  ) {
    this._getBackendUrl = getBackendUrl;
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
    const baseUrl = this._getBackendUrl();
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

export interface VMInfo {
  name: string;
  status: string;
  containerAppLabels: string[];
  properties: { label: string; value: string }[];
}

export class VMTreeItem extends vscode.TreeItem {
  constructor(
    public readonly label: string,
    public readonly collapsibleState: vscode.TreeItemCollapsibleState,
    public readonly vmInfo?: VMInfo,
    public readonly containerInfo?: ContainerInfo,
    public readonly isVMInfoGroup?: boolean,
  ) {
    super(label, collapsibleState);
  }
}

export class VMsTreeProvider implements vscode.TreeDataProvider<VMTreeItem> {
  private _onDidChangeTreeData = new vscode.EventEmitter<VMTreeItem | undefined>();
  readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

  private vms: VMInfo[] = [];
  private initialized = false;
  private _visible = false;
  private _getBackendUrl: () => string | undefined;
  private _getGitHubSession: () => Promise<vscode.AuthenticationSession | undefined>;
  private _getContainers: () => ContainerInfo[];

  constructor(
    getBackendUrl: () => string | undefined,
    getGitHubSession: () => Promise<vscode.AuthenticationSession | undefined>,
    getContainers: () => ContainerInfo[]
  ) {
    this._getBackendUrl = getBackendUrl;
    this._getGitHubSession = getGitHubSession;
    this._getContainers = getContainers;
  }

  get visible(): boolean { return this._visible; }

  async refresh(): Promise<void> {
    const result = await this.fetchVMs();
    this.vms = result.vms;
    this._visible = result.visible;
    this.initialized = true;
    this._onDidChangeTreeData.fire(undefined);
  }

  getTreeItem(element: VMTreeItem): vscode.TreeItem {
    return element;
  }

  getChildren(element?: VMTreeItem): VMTreeItem[] {
    if (!element) {
      if (!this.initialized) { return []; }
      if (this.vms.length === 0) {
        const empty = new VMTreeItem('No Windows VMs found', vscode.TreeItemCollapsibleState.None);
        empty.iconPath = new vscode.ThemeIcon('info');
        return [empty];
      }
      return this.vms.map(vm => {
        const statusLower = vm.status.toLowerCase();
        const icon = statusLower === 'ready'
          ? new vscode.ThemeIcon('vm-running', new vscode.ThemeColor('testing.iconPassed'))
          : new vscode.ThemeIcon('vm', new vscode.ThemeColor('testing.iconFailed'));

        const item = new VMTreeItem(
          `${vm.name} (${vm.status})`,
          vscode.TreeItemCollapsibleState.Collapsed,
          vm
        );
        item.iconPath = icon;
        item.tooltip = `${vm.name}\nStatus: ${vm.status}`;
        item.contextValue = 'windowsVM';
        const allContainers = this._getContainers();
        const vmContainerLabels = new Set(vm.containerAppLabels);
        const matchedContainerCount = allContainers.filter(p => vmContainerLabels.has(p.appLabel)).length;
        if (matchedContainerCount > 0) {
          item.description = `${matchedContainerCount} container${matchedContainerCount !== 1 ? 's' : ''}`;
        }
        return item;
      });
    }

    // Children of a VM: "VM Info" group + containers
    if (element.vmInfo && !element.containerInfo && !element.isVMInfoGroup) {
      const children: VMTreeItem[] = [];

      // Collapsible "VM Info" group
      const infoItem = new VMTreeItem(
        'VM Info',
        vscode.TreeItemCollapsibleState.Collapsed,
        element.vmInfo,
        undefined,
        true
      );
      infoItem.iconPath = new vscode.ThemeIcon('info');
      children.push(infoItem);

      // Containers on this VM
      const allContainers = this._getContainers();
      const vmContainerLabels = new Set(element.vmInfo.containerAppLabels);
      const vmContainers = allContainers.filter(p => vmContainerLabels.has(p.appLabel));
      for (const container of vmContainers) {
        const statusLower = container.status.toLowerCase();
        const containerIcon = statusLower.startsWith('running')
          ? new vscode.ThemeIcon('vm-running', new vscode.ThemeColor('testing.iconPassed'))
          : statusLower.startsWith('starting')
            ? new vscode.ThemeIcon('sync~spin', new vscode.ThemeColor('testing.iconQueued'))
            : new vscode.ThemeIcon('vm', new vscode.ThemeColor('testing.iconSkipped'));

        const containerItem = new VMTreeItem(
          `${container.name} (${container.status})`,
          vscode.TreeItemCollapsibleState.Collapsed,
          undefined,
          container
        );
        containerItem.iconPath = containerIcon;
        containerItem.tooltip = `${container.appLabel}\nStatus: ${container.status}`;
        containerItem.contextValue = `container-${container.status.toLowerCase()}`;
        children.push(containerItem);
      }

      return children;
    }

    // Children of the "VM Info" group: VM properties
    if (element.isVMInfoGroup && element.vmInfo) {
      return element.vmInfo.properties.map(prop => {
        const child = new VMTreeItem(
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

        return child;
      });
    }

    // Children of a container under a VM: properties
    if (element.containerInfo) {
      return element.containerInfo.properties.map(prop => {
        const child = new VMTreeItem(
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

  private async fetchVMs(): Promise<{ vms: VMInfo[]; visible: boolean }> {
    const baseUrl = this._getBackendUrl();
    if (!baseUrl) { return { vms: [], visible: false }; }

    const session = await this._getGitHubSession();
    if (!session) { return { vms: [], visible: false }; }

    try {
      const response = await fetch(`${baseUrl}/ListVMs`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${session.accessToken}`,
        },
        body: JSON.stringify({ parameters: {} }),
      });

      if (response.status === 403 || response.status === 500) {
        // Not admin — hide the view
        return { vms: [], visible: false };
      }

      if (!response.ok) { return { vms: [], visible: false }; }

      const result = await response.json() as { message: string };
      return { vms: this.parseVMsMessage(result.message), visible: true };
    } catch {
      return { vms: [], visible: false };
    }
  }

  private parseVMsMessage(message: string): VMInfo[] {
    const vms: VMInfo[] = [];
    const lines = message.split(/\r?\n/);

    let current: VMInfo | undefined;

    for (const line of lines) {
      const headerMatch = line.match(/^  (\S+)\s*$/);
      if (headerMatch) {
        if (current) { vms.push(current); }
        current = {
          name: headerMatch[1],
          status: 'Unknown',
          containerAppLabels: [],
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
        } else if (key === 'Containers') {
          current.containerAppLabels = value === '0' ? [] : value.split(',').map(s => s.trim());
          continue;
        }
        current.properties.push({ label: key, value });
      }
    }

    if (current) { vms.push(current); }
    return vms;
  }
}
