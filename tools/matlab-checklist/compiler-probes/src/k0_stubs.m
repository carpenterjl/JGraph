function k0_stubs(varargin)
%K0_STUBS The interactive functions with a closed stdin: pause, input, keyboard, and the
%stub versions of clc and home. Run with a short timeout: a wait here is a finding too.
k0_quiet;
fprintf('start\n');
k0_do('pause.0.1', @() pause(0.1));
k0_try('pause.query', @() pause('query'));
k0_do('pause.on', @() pause('on'));
k0_do('clc', @() clc);
k0_do('home', @() home);
k0_try('input.number', @() input('k0 number? '));
k0_try('input.s', @() input('k0 text? ', 's'));
k0_do('keyboard', @() keyboard);
fprintf('before.pause.noargs\n');
k0_do('pause.noargs', @() pause);
fprintf('end\n');
end
