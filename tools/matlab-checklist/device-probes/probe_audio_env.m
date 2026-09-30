% PROBE_AUDIO_ENV  Audio devices and the two objects' shapes in R2025b (device classes plan, stage D10).
%   Enumerates and constructs only: nothing records from a microphone and nothing is heard (no play).
import multimedia.internal.audio.device.DeviceInfo
dv_pr('ver', 'version')
dv_pr('info', 'audiodevinfo')
info = audiodevinfo;
for k = 1:numel(info.input)
    dv_pr(sprintf('in%d', k), sprintf('info.input(%d)', k))
    dv_pr(sprintf('in%d_name', k), sprintf('info.input(%d).Name', k))
    dv_pr(sprintf('in%d_drv', k), sprintf('info.input(%d).DriverVersion', k))
    dv_pr(sprintf('in%d_id', k), sprintf('info.input(%d).ID', k))
end
for k = 1:numel(info.output)
    dv_pr(sprintf('out%d_name', k), sprintf('info.output(%d).Name', k))
    dv_pr(sprintf('out%d_drv', k), sprintf('info.output(%d).DriverVersion', k))
    dv_pr(sprintf('out%d_id', k), sprintf('info.output(%d).ID', k))
end
dv_pr('in_size', 'size(info.input)')
dv_pr('count_in', 'audiodevinfo(1)')
dv_pr('count_out', 'audiodevinfo(0)')
dv_pr('count_bad', 'audiodevinfo(2)')
dv_pr('count_char', 'audiodevinfo(''a'')')
if ~isempty(info.output)
    dv_pr('name_out', sprintf('audiodevinfo(0, %d)', info.output(1).ID))
    dv_pr('drv_out', sprintf('audiodevinfo(0, %d, 1)', info.output(1).ID))
    dv_pr('id_out', sprintf('audiodevinfo(0, ''%s'')', info.output(1).Name))
    dv_pr('id_out_part', sprintf('audiodevinfo(0, ''%s'')', info.output(1).Name(1:4)))
    dv_pr('find_out', 'audiodevinfo(0, 44100, 16, 2)')
    dv_pr('supports_out', sprintf('audiodevinfo(0, %d, 44100, 16, 2)', info.output(1).ID))
end
if ~isempty(info.input)
    dv_pr('name_in', sprintf('audiodevinfo(1, %d)', info.input(1).ID))
    dv_pr('name_in_as_out', sprintf('audiodevinfo(0, %d)', info.input(1).ID))
end
dv_pr('name_bad_id', 'audiodevinfo(1, 999)')
dv_pr('id_bad_name', 'audiodevinfo(1, ''no such device'')')
dv_pr('six', 'audiodevinfo(1,2,3,4,5,6)')
dv_pr('all_devs', 'DeviceInfo.getDevicesForDefaultHostApi()')
devs = DeviceInfo.getDevicesForDefaultHostApi();
dv_px('dev1', 'disp(devs(1))')
dv_px('dev_props', 'disp(properties(devs(1)))')
dv_px('di_methods', 'disp(methods(''multimedia.internal.audio.device.DeviceInfo''))')
dv_pr('n_devs', 'numel(devs)')
for k = 1:numel(devs)
    dv_px(sprintf('dev%d_all', k), sprintf('disp(devs(%d))', k))
end
dv_px('all_hosts', 'disp(DeviceInfo.getDevices())')
dv_pr('default_in', 'DeviceInfo.getDefaultInputDeviceID()')
dv_pr('default_out', 'DeviceInfo.getDefaultOutputDeviceID()')

% The objects, constructed and shown, never started.
p = audioplayer(zeros(1000, 1), 8000);
dv_px('p_disp', 'p')
dv_px('p_get', 'get(p)')
dv_px('p_props', 'disp(properties(p))')
dv_px('p_methods', 'disp(methods(p))')
dv_pr('p_class', 'class(p)')
dv_pr('p_super', 'superclasses(p)')
r = audiorecorder;
dv_px('r_disp', 'r')
dv_px('r_get', 'get(r)')
dv_px('r_props', 'disp(properties(r))')
dv_px('r_methods', 'disp(methods(r))')
dv_pr('r_class', 'class(r)')
