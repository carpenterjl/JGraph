function out = webcam_sim_case(kind)
% WEBCAM_SIM_CASE  The cases of webcam_sim (device classes plan, stage D11, ADR 0195): webcamlist,
%   webcam, snapshot, preview and closePreview on jgraph.internal.camsim's cameras. "JGraph Test
%   Camera" has four resolutions and six controls, three with a Mode; "JGraph Second Camera" has one
%   resolution and a Brightness. A frame's red rises left to right and its green top to bottom, its
%   blue steps by eight a frame, and Brightness away from its default lifts all three. The answers are
%   written from that rule. Answers text.
jgraph.internal.camsim('on');
first = 'JGraph Test Camera';
second = 'JGraph Second Camera';
try
    switch kind
        case 'list'
            l = webcamlist;
            out = sprintf('%s %d %d; %s; %s', class(l), size(l), l{1}, l{2});
        case 'first'
            cam = webcam;
            out = sprintf('%s; %s; %s; %d %d', class(cam), cam.Name, cam.Resolution, isvalid(cam), isa(cam, 'handle'));
        case 'disp'
            cam = webcam;
            out = ix_flat(evalc('disp(cam)'));
        case 'properties'
            cam = webcam;
            out = strjoin(properties(cam)', ' ');
        case 'methods'
            cam = webcam;
            out = strjoin(methods(cam)', ' ');
        case 'resolutions'
            cam = webcam;
            r = cam.AvailableResolutions;
            out = sprintf('%s %d %d: %s', class(r), size(r), strjoin(r, ' '));
        case 'by_index'
            cam = webcam(2);
            out = [cam.Name '; ' strjoin(properties(cam)', ' ')];
        case 'by_name'
            a = webcam(second);
            name1 = a.Name;
            clear a
            b = webcam('second');
            name2 = b.Name;
            clear b
            c = webcam("JGRAPH TEST CAMERA");
            out = sprintf('%s; %s; %s', name1, name2, c.Name);
        case 'name_value'
            cam = webcam(1, 'Resolution', '320x240', 'Brightness', 100, 'ExposureMode', 'manual');
            out = sprintf('%s %d %s %s', cam.Resolution, cam.Brightness, cam.ExposureMode, mat2str(size(snapshot(cam))));
        case 'pairs_only'
            cam = webcam('Resolution', '160x120');
            out = sprintf('%s %s', cam.Name, cam.Resolution);
        case 'bad_index'
            out = [dv_err(@() webcam(3)) ' // ' dv_err(@() webcam(0)) ' // ' dv_err(@() webcam(1.5)) ' // ' dv_err(@() webcam({1}))];
        case 'bad_name'
            out = [dv_err(@() webcam('nope')) ' // ' dv_err(@() webcam('JGraph')) ' // ' dv_err(@() webcam(''))];
        case 'bad_pairs'
            out = [dv_err(@() webcam(1, 'NoSuch', 1)) ' // ' dv_err(@() webcam(1, 2, 3)) ' // ' dv_err(@() webcam(1, 'Resolution', '1x1')) ...
                ' // ' dv_err(@() webcam(1, 'Name', 'x'))];
        case 'bad_pairs_free'
            % A constructor that refuses its pairs leaves the camera free.
            dv_err(@() webcam(1, 'Brightness', 999));
            n = jgraph.internal.camsim('open');
            cam = webcam(1);
            out = sprintf('%d %s', n, cam.Name);
        case 'one_object'
            cam = webcam(1);
            refused = dv_err(@() webcam(1));
            other = webcam(2);
            clear cam
            again = webcam(first);
            out = sprintf('%s // %s %s', refused, other.Name, again.Name);
        case 'snapshot'
            cam = webcam;
            img = snapshot(cam);
            out = sprintf('%s %s / %s %s %s', class(img), mat2str(size(img)), mat2str(squeeze(img(1, 1, :))'), ...
                mat2str(squeeze(img(end, end, :))'), mat2str(squeeze(img(240, 320, :))'));
        case 'frames'
            % Each snapshot is a new frame: blue steps by eight.
            cam = webcam;
            a = snapshot(cam);
            b = snapshot(cam);
            c = cam.snapshot();
            out = sprintf('%d %d %d', a(1, 1, 3), b(1, 1, 3), c(1, 1, 3));
        case 'timestamp'
            cam = webcam;
            before = datetime('now');
            [img, t] = snapshot(cam);
            out = sprintf('%s %d %d %s', class(t), numel(t), abs(seconds(t - before)) < 5, mat2str(size(img)));
        case 'snapshot_args'
            cam = webcam;
            out = [dv_err(@() snapshot(cam, 1)) ' // ' webcam_outputs(cam)];
        case 'resolution'
            cam = webcam;
            cam.Resolution = '320x240';
            s1 = size(snapshot(cam));
            cam.Resolution = "1280x720";
            s2 = size(snapshot(cam));
            set(cam, 'Resolution', '160X120');
            s3 = size(snapshot(cam));
            out = sprintf('%s %s %s %s', mat2str(s1), mat2str(s2), mat2str(s3), get(cam, 'Resolution'));
        case 'resolution_bad'
            cam = webcam;
            out = [webcam_set(cam, 'Resolution', '1x1') ' // ' webcam_set(cam, 'Resolution', 640) ' // ' ...
                webcam_set(cam, 'AvailableResolutions', {'1x1'}) ' // ' cam.Resolution];
        case 'brightness'
            % Ten above the default lifts every channel by ten; the bright corner stays at 255.
            cam = webcam;
            cam.Brightness = 138;
            img = snapshot(cam);
            cam.Brightness = 118;
            dim = snapshot(cam);
            out = sprintf('%s %s / %s %s', mat2str(squeeze(img(1, 1, :))'), mat2str(squeeze(img(end, end, 1:2))'), ...
                mat2str(squeeze(dim(1, 1, :))'), mat2str(squeeze(dim(end, end, 1:2))'));
        case 'control_values'
            cam = webcam;
            cam.Contrast = 0;
            cam.Zoom = 500;
            cam.Brightness = uint8(7);
            out = sprintf('%d %d %d %s', cam.Contrast, cam.Zoom, cam.Brightness, class(cam.Brightness));
        case 'control_bad'
            cam = webcam;
            out = [webcam_set(cam, 'Brightness', 256) ' // ' webcam_set(cam, 'Brightness', -1) ' // ' webcam_set(cam, 'Brightness', 1.5) ...
                ' // ' webcam_set(cam, 'Brightness', 'a') ' // ' webcam_set(cam, 'Brightness', [1 2]) ' // ' webcam_set(cam, 'Zoom', 99) ...
                ' // ' webcam_set(cam, 'NoSuch', 1) ' // ' dv_err(@() cam.NoSuch) sprintf(' // %d', cam.Brightness)];
        case 'modes'
            cam = webcam;
            m0 = [cam.ExposureMode ' ' cam.FocusMode ' ' cam.WhiteBalanceMode];
            refused = webcam_set(cam, 'Exposure', -3);
            cam.ExposureMode = 'manual';
            cam.Exposure = -3;
            e1 = cam.Exposure;
            cam.ExposureMode = "auto";
            out = sprintf('%s // %s // %d %s %s', m0, refused, e1, cam.ExposureMode, webcam_set(cam, 'Exposure', -4));
        case 'mode_bad'
            cam = webcam;
            cam.FocusMode = 'man';
            out = [cam.FocusMode ' // ' webcam_set(cam, 'FocusMode', 'sideways') ' // ' webcam_set(cam, 'FocusMode', 1) ...
                ' // ' webcam_set(cam, 'BrightnessMode', 'auto')];
        case 'get_set'
            cam = webcam;
            set(cam, 'brightness', 9);
            out = sprintf('%d %s %s', get(cam, 'BRIGHTNESS'), get(cam, 'Name'), dv_err(@() get(cam, 'NoSuch')));
        case 'deleted'
            cam = webcam;
            delete(cam);
            n = jgraph.internal.camsim('open');
            again = webcam(1);
            out = [sprintf('%d %d %s|', isvalid(cam), n, again.Name) dv_err(@() snapshot(cam)) ' // ' dv_err(@() cam.Resolution) ...
                ' // ' ix_flat(evalc('disp(cam)'))];
        case 'clear'
            cam = webcam;
            other = webcam(2);
            n2 = jgraph.internal.camsim('open');
            clear cam
            n1 = jgraph.internal.camsim('open');
            other = 5;
            out = sprintf('%d %d %d %d', n2, n1, jgraph.internal.camsim('open'), other);
        case 'unplug'
            cam = webcam(first);
            jgraph.internal.camsim('unplug', first);
            l = webcamlist;
            lost = dv_err(@() snapshot(cam));
            clear cam
            gone = dv_err(@() webcam(first));
            now1 = webcam;
            jgraph.internal.camsim('plug', first);
            back = numel(webcamlist);
            out = sprintf('%d %s // %s // %s // %s %d', numel(l), l{1}, lost, gone, now1.Name, back);
        case 'none'
            jgraph.internal.camsim('unplug', first);
            jgraph.internal.camsim('unplug', second);
            l = webcamlist;
            out = [sprintf('%s %d %d|', class(l), size(l)) dv_err(@() webcam) ' // ' dv_err(@() webcam(1))];
        case 'list_args'
            out = dv_err(@() webcamlist(1));
        case 'preview'
            % The preview is a figure of its own, numbered from 1001; the figure a script draws into
            % stays current.
            close all
            mine = figure;
            cam = webcam;
            n0 = numel(findobj('Type', 'figure'));
            preview(cam);
            n1 = numel(findobj('Type', 'figure'));
            same = isequal(gcf, mine) && ishandle(1001);
            pause(0.2);
            preview(cam);                     % a second call opens no second figure
            n2 = numel(findobj('Type', 'figure'));
            cam.Resolution = '320x240';       % the preview follows a change of resolution
            pause(0.2);
            closePreview(cam);
            n3 = numel(findobj('Type', 'figure'));
            closePreview(cam);                % with none open, nothing happens
            gone = ~ishandle(1001);
            close(mine);
            out = sprintf('%d %d %d %d %d', n1 - n0, same, n2 - n0, n3 - n0, gone);
        case 'preview_delete'
            % Deleting the camera closes its preview; a preview whose figure was closed is simply over.
            % With no figure open, the preview is not left current: gcf opens figure 1.
            close all
            cam = webcam;
            preview(cam);
            pause(0.1);
            f = gcf;
            n1 = numel(findobj('Type', 'figure'));
            delete(cam);
            n2 = numel(findobj('Type', 'figure'));
            other = webcam(2);
            preview(other);
            close(1001);
            pause(0.1);
            img = snapshot(other);
            closePreview(other);
            n3 = numel(findobj('Type', 'figure'));
            first1 = isequal(f, figure(1));
            close(f);
            out = [sprintf('%d %d %d %d %s ', first1, n1, n2, n3, mat2str(size(img))) dv_err(@() preview(other, 1))];
        otherwise
            error('webcam_sim_case:unknown', 'unknown case %s', kind);
    end
catch e
    out = ['ERR ' e.identifier ' ' e.message];
end
clear cam other again now1 a b c
jgraph.internal.camsim('off');
out = strrep(out, '|', '/'); % a fixture value holds no |
end

function out = webcam_set(cam, name, value)
% cam.(name) = value, answering 'ok' or the refusal.
try
    cam.(name) = value;
    out = 'ok';
catch e
    out = [e.identifier ' ## ' ix_flat(e.message)];
end
end

function out = webcam_outputs(cam)
% Three outputs asked of snapshot, which has two.
try
    [a, b, c] = snapshot(cam); %#ok<ASGLU>
    out = 'none';
catch e
    out = [e.identifier ' ## ' ix_flat(e.message)];
end
end
