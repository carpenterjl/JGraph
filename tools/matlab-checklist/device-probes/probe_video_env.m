% PROBE_VIDEO_ENV  What R2025b has for cameras on this machine (device classes plan, stage D11).
%   Nothing is acquired: no object is created, no preview opened, no frame taken.
dv_pr('which_webcam', 'which(''webcam'')')
dv_pr('which_webcamlist', 'which(''webcamlist'')')
dv_pr('exist_webcam', 'exist(''webcam'')')
dv_pr('exist_webcamlist', 'exist(''webcamlist'')')
dv_pr('webcamlist', 'webcamlist')
dv_pr('webcam', 'webcam')
dv_pr('webcam_1', 'webcam(1)')
dv_pr('webcam_name', 'webcam(''no such camera'')')
dv_px('webcamlist_px', 'webcamlist')
dv_px('webcam_px', 'c = webcam')
dv_pr('lic_imaq', 'license(''test'', ''Image_Acquisition_Toolbox'')')
dv_pr('which_videoinput', 'which(''videoinput'')')
dv_pr('which_imaqhwinfo', 'which(''imaqhwinfo'')')
dv_pr('which_snapshot', 'which(''snapshot'')')
dv_pr('which_preview', 'which(''preview'')')
dv_pr('which_closePreview', 'which(''closePreview'')')
dv_pr('which_ipcam', 'which(''ipcam'')')
dv_pr('which_imaqreset', 'which(''imaqreset'')')
dv_pr('which_imaqfind', 'which(''imaqfind'')')
dv_px('imaqhwinfo_px', 'imaqhwinfo')
dv_pr('imaqhwinfo', 'imaqhwinfo')
try
    h = imaqhwinfo;
    dv_pr('adaptors', 'h.InstalledAdaptors')
    dv_pr('adaptors_class', 'class(h.InstalledAdaptors)')
    dv_pr('adaptors_size', 'size(h.InstalledAdaptors)')
    dv_pr('tb_name', 'h.ToolboxName')
    dv_pr('tb_version', 'h.ToolboxVersion')
    dv_pr('ml_version', 'h.MATLABVersion')
catch e
    dv_pr('imaqhwinfo_error', 'e.message')
end
dv_pr('hw_winvideo', 'imaqhwinfo(''winvideo'')')
dv_pr('hw_bogus', 'imaqhwinfo(''bogus'')')
dv_pr('vi_none', 'videoinput')
dv_pr('vi_winvideo', 'videoinput(''winvideo'')')
dv_pr('vi_bogus', 'videoinput(''bogus'')')
try
    sp = matlabshared.supportpkg.getInstalled;
    dv_pr('sp_count', 'numel(sp)')
    for k = 1:numel(sp)
        dv_pr(sprintf('sp%d', k), sprintf('sp(%d).Name', k))
    end
catch e
    dv_pr('sp_error', 'e.message')
end
dv_pr('root_webcam', 'exist(fullfile(matlabroot, ''toolbox'', ''matlab'', ''webcam''), ''dir'')')
dv_pr('dir_imaq', 'exist(fullfile(matlabroot, ''toolbox'', ''imaq''), ''dir'')')
