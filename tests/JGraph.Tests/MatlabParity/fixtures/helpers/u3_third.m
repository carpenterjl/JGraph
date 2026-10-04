function v = u3_third(c)
% Asks textwrap for three outputs, which it refuses (U3 fixtures).
[a, b, v] = textwrap(c, {'one two'}); %#ok<ASGLU>
end
