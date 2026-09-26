% probe_net_isnet: NET.isNETSupported before dotnetenv. Does it load .NET, and which runtime?
ip_px('dotnetenv.fresh', 'dotnetenv');
ip_pr('isNETSupported', 'NET.isNETSupported');
ip_px('dotnetenv.after.isNETSupported', 'dotnetenv');
ip_px('dotnetenv.core.after', 'dotnetenv("core", Version="8")');
