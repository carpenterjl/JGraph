function makers = u8_makers()
% The U8 objects by a key, each made in a parent figure p (classic or uifigure).
makers = {
    'Table',        @(p) uitable(p)
    'TabGroup',     @(p) uitabgroup(p)
    'Tab',          @(p) uitab(uitabgroup(p))
    'Menu',         @(p) uimenu(p)
    'SubMenu',      @(p) uimenu(uimenu(p))
    'ContextMenu',  @(p) uicontextmenu(p)
    'Toolbar',      @(p) uitoolbar(p)
    'PushTool',     @(p) uipushtool(uitoolbar(p))
    'ToggleTool',   @(p) uitoggletool(uitoolbar(p))
    };
end
