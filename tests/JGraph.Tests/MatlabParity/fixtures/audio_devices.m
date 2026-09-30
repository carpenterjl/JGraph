% audio_devices.m -- audiodevinfo and audiodevreset on this machine's audio devices (device classes
% plan, stage D10). Recorded from R2025b, whose PortAudio DirectSound host numbers DirectSound's
% capture list and then its playback list, each led by its primary driver; JGraph enumerates the same
% two lists. The names are this machine's (a Realtek codec and ASUS's noise-cancelling pair), so the
% fixture is recorded here and agrees only where the same devices are. Nothing records: the input
% forms that would try a microphone (four and five arguments with IO 1) are not asked. The output
% forms play zeros for a moment, which nothing hears.

info = audiodevinfo;
ix_chk('fields', strjoin(fieldnames(info)', ','));
ix_chk('in_size', mat2str(size(info.input)));
ix_chk('out_size', mat2str(size(info.output)));
ix_chk('in_fields', strjoin(fieldnames(info.input)', ','));
for k = 1:numel(info.input)
    ix_chk(sprintf('in%d', k), sprintf('%d %s | %s', info.input(k).ID, info.input(k).Name, info.input(k).DriverVersion));
end
for k = 1:numel(info.output)
    ix_chk(sprintf('out%d', k), sprintf('%d %s | %s', info.output(k).ID, info.output(k).Name, info.output(k).DriverVersion));
end
ix_chk('name_class', class(info.output(1).Name));
ix_chk('id_class', class(info.output(1).ID));
ix_chk('count_in', sprintf('%d', audiodevinfo(1)));
ix_chk('count_out', sprintf('%d', audiodevinfo(0)));
ix_chk('count_bad', dv_err(@() audiodevinfo(2)));
ix_chk('count_char', dv_err(@() audiodevinfo('a')));
ix_chk('count_empty', dv_err(@() audiodevinfo([])));
out = info.output(1).ID;
ix_chk('name_out', audiodevinfo(0, out));
ix_chk('name_in', audiodevinfo(1, info.input(1).ID));
ix_chk('driver_out', audiodevinfo(0, out, 'DriverVersion'));
ix_chk('id_by_name', sprintf('%d', audiodevinfo(0, info.output(2).Name)));
ix_chk('id_by_part', sprintf('%d', audiodevinfo(0, 'Primary')));
ix_chk('name_in_as_out', dv_err(@() audiodevinfo(0, info.input(1).ID)));
ix_chk('name_bad_id', dv_err(@() audiodevinfo(1, 999)));
ix_chk('name_frac_id', dv_err(@() audiodevinfo(0, out + 0.5)));
ix_chk('driver_bad_id', dv_err(@() audiodevinfo(0, 999, 'DriverVersion')));
ix_chk('id_bad_name', dv_err(@() audiodevinfo(1, 'no such device')));
ix_chk('id_case', dv_err(@() audiodevinfo(0, 'primary sound driver')));
ix_chk('id_many', dv_err(@() audiodevinfo(0, 'Windows DirectSound')));
ix_chk('six', dv_err(@() disp(audiodevinfo(1, 2, 3, 4, 5, 6))));
ix_chk('find_out', sprintf('%d', audiodevinfo(0, 44100, 16, 2)));
ix_chk('find_out_none', sprintf('%d', audiodevinfo(0, 44100, 12, 2)));
ix_chk('supports_out', mat2str(audiodevinfo(0, out, 44100, 16, 2)));
ix_chk('supports_bits', mat2str(audiodevinfo(0, out, 44100, 12, 2)));
ix_chk('supports_three_ch', mat2str(audiodevinfo(0, out, 44100, 16, 3)));
ix_chk('supports_in_id', mat2str(audiodevinfo(0, info.input(1).ID, 44100, 16, 2)));
audiodevreset;
ix_chk('after_reset', sprintf('%d %d', audiodevinfo(1), audiodevinfo(0)));
