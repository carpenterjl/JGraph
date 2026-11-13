function v = oi169_unlockme()
% Locks and unlocks its own file, bare and by name, answering the lock at each step (oi169_session_questions).
mlock;
a = mislocked;
munlock;
b = mislocked;
mlock;
munlock('oi169_unlockme');
c = mislocked;
v = [a b c];
end
