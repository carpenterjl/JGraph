function n = oi169_lockme()
% Locks its own file and counts its calls in a persistent: [count, mislocked] (oi169_session_questions).
persistent count
mlock;
if isempty(count)
    count = 0;
end
count = count + 1;
n = [count, double(mislocked)];
end
