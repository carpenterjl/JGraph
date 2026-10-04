function ex1_uicontrol_nested
% EX1_UICONTROL_NESTED  Classic programmatic GUI: figure + uicontrol + axes,
% callbacks are nested functions that share the parent's variables.
% Pattern seen in lab handouts and File Exchange tools from ~2005 onward.

freq = 1;            % shared state, captured by the nested callbacks
amp  = 1;
shape = 'sin';

fig = figure('Name', 'Signal explorer', 'NumberTitle', 'off', ...
    'MenuBar', 'none', 'ToolBar', 'none', 'Units', 'pixels', ...
    'Position', [200 200 640 420], 'CloseRequestFcn', @onClose);

ax = axes('Parent', fig, 'Units', 'normalized', 'Position', [0.08 0.30 0.88 0.65]);
t = linspace(0, 2, 500);
hLine = plot(ax, t, sin(2*pi*freq*t), 'LineWidth', 1.5);
grid(ax, 'on'); xlabel(ax, 't (s)'); ylabel(ax, 'y');

uicontrol(fig, 'Style', 'text', 'String', 'Frequency (Hz)', ...
    'Units', 'normalized', 'Position', [0.02 0.14 0.16 0.05], 'HorizontalAlignment', 'left');
hSlider = uicontrol(fig, 'Style', 'slider', 'Min', 0.1, 'Max', 10, 'Value', freq, ...
    'SliderStep', [0.01 0.1], 'Units', 'normalized', 'Position', [0.18 0.14 0.45 0.05], ...
    'Callback', @onSlider);
hFreqText = uicontrol(fig, 'Style', 'text', 'String', sprintf('%.2f', freq), ...
    'Units', 'normalized', 'Position', [0.64 0.14 0.08 0.05]);

uicontrol(fig, 'Style', 'text', 'String', 'Amplitude', ...
    'Units', 'normalized', 'Position', [0.02 0.06 0.16 0.05], 'HorizontalAlignment', 'left');
hAmp = uicontrol(fig, 'Style', 'edit', 'String', num2str(amp), ...
    'Units', 'normalized', 'Position', [0.18 0.06 0.12 0.06], 'Callback', @onAmp);

uicontrol(fig, 'Style', 'popupmenu', 'String', {'sin', 'square', 'sawtooth'}, ...
    'Units', 'normalized', 'Position', [0.34 0.06 0.18 0.06], 'Callback', @onShape);

hGrid = uicontrol(fig, 'Style', 'checkbox', 'String', 'Grid', 'Value', 1, ...
    'Units', 'normalized', 'Position', [0.55 0.06 0.10 0.06], ...
    'Callback', @(src, ~) grid(ax, onOff(get(src, 'Value'))));

uicontrol(fig, 'Style', 'pushbutton', 'String', 'Reset', ...
    'Units', 'normalized', 'Position', [0.78 0.06 0.18 0.08], 'Callback', @onReset);

    function onSlider(src, ~)
        freq = get(src, 'Value');
        set(hFreqText, 'String', sprintf('%.2f', freq));
        redraw();
    end

    function onAmp(src, ~)
        v = str2double(get(src, 'String'));
        if isnan(v) || v <= 0
            errordlg('Amplitude must be a positive number.', 'Bad input', 'modal');
            set(src, 'String', num2str(amp));
            return
        end
        amp = v;
        redraw();
    end

    function onShape(src, ~)
        items = get(src, 'String');
        shape = items{get(src, 'Value')};
        redraw();
    end

    function onReset(~, ~)
        freq = 1; amp = 1;
        set(hSlider, 'Value', freq);
        set(hFreqText, 'String', sprintf('%.2f', freq));
        set(hAmp, 'String', '1');
        set(hGrid, 'Value', 1); grid(ax, 'on');
        redraw();
    end

    function redraw()
        phase = 2*pi*freq*t;
        switch shape
            case 'sin',      y = sin(phase);
            case 'square',   y = sign(sin(phase));
            case 'sawtooth', y = 2*mod(phase/(2*pi), 1) - 1;
        end
        set(hLine, 'YData', amp*y);
        title(ax, sprintf('%s, %.2f Hz', shape, freq));
        drawnow limitrate
    end

    function onClose(src, ~)
        selection = questdlg('Close the explorer?', 'Close', 'Yes', 'No', 'Yes');
        if strcmp(selection, 'Yes')
            delete(src);
        end
    end
end

function s = onOff(tf)
if tf, s = 'on'; else, s = 'off'; end
end
