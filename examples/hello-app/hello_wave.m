function [x, y] = hello_wave(shape, frequency, amplitude)
%HELLO_WAVE One period's worth of a named wave, for the Hello World app.
%   [x, y] = hello_wave(shape, frequency, amplitude) samples two seconds of a
%   'sine', 'square' or 'damped' wave of the given frequency in hertz and
%   amplitude. The app's Plot button calls it; it lives in its own file so a
%   deployed app shows a function found by dependency analysis, not only the
%   app class itself.
arguments
    shape (1, :) char {mustBeMember(shape, {'sine', 'square', 'damped'})} = 'sine'
    frequency (1, 1) double {mustBePositive} = 1
    amplitude (1, 1) double {mustBeNonnegative} = 1
end
x = linspace(0, 2, 400);
phase = 2 * pi * frequency * x;
switch shape
    case 'sine'
        y = amplitude * sin(phase);
    case 'square'
        y = amplitude * sign(sin(phase));
    case 'damped'
        y = amplitude * exp(-x) .* sin(phase);
end
end
