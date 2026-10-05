function h = u8_make(kind, parent)
% One object of U8 by its class word, in a figure of either kind (U8 fixtures). A tab gets a tab
% group of its own and a tool a toolbar, because those are the only parents they take.
switch kind
    case 'Table',       h = uitable(parent);
    case 'TabGroup',    h = uitabgroup(parent);
    case 'Tab',         h = uitab(uitabgroup(parent));
    case 'Menu',        h = uimenu(parent);
    case 'SubMenu',     h = uimenu(uimenu(parent));
    case 'ContextMenu', h = uicontextmenu(parent);
    case 'Toolbar',     h = uitoolbar(parent);
    case 'PushTool',    h = uipushtool(uitoolbar(parent));
    case 'ToggleTool',  h = uitoggletool(uitoolbar(parent));
end
end
