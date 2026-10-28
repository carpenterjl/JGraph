"""The acceptance set's server client (compiler and SDK plan, stage K22): the archive accarch built
from src\\k22, served by R2025b's muserve or by jgraph-mps, asked the discovery API, synchronous
calls of every function in both notations, and the asynchronous protocol.

    python acc_server.py <base url> <component>

One label<TAB>result line per question. The server's own ids and sequence numbers are tokens, an
error's file path keeps its name alone, and headers that vary by server are dropped."""
import http.client
import json
import re
import sys
import time
from urllib.parse import urlparse

BASE = sys.argv[1]
COMPONENT = sys.argv[2]
parsed = urlparse(BASE)
HOST, PORT = parsed.hostname, parsed.port or 80
DROP = {'date', 'server', 'connection', 'keep-alive', 'transfer-encoding', 'content-length', 'x-mps-start-time'}


def normalize(text):
    text = re.sub(r'\(request id=[0-9:]+\)', '(request id=<N>)', text)
    text = re.sub(r'"file":"(?:[^"\\]|\\\\|\\/)*?[\\/]+([^"\\/]+)"', r'"file":"<F>/\1"', text)
    text = re.sub(r'"file":"([A-Za-z0-9_]+\.m)"', r'"file":"<F>/\1"', text)
    text = re.sub(r'/~[^/"]+/requests/[0-9a-f-]{36}', '/~<CLIENT>/requests/<ID>', text)
    text = re.sub(r'"id":"[0-9a-f-]{36}"', '"id":"<ID>"', text)
    text = re.sub(r'"(lastModifiedSeq|createdSeq)":[0-9]+', r'"\1":<SEQ>', text)
    text = re.sub(r'"since":[0-9]+', '"since":<SEQ>', text)
    return ''.join(c if 32 <= ord(c) < 127 or c == '\t' else '\\u%04x' % ord(c) for c in text)


def say(label, text):
    print(normalize('%s\t%s' % (label, text)), flush=True)


def send(method, path, body=None, headers=None):
    if body is not None and not isinstance(body, (bytes, bytearray)):
        body = json.dumps(body, separators=(',', ':')).encode('utf-8')
    hdrs = {'Content-Type': 'application/json'} if body is not None else {}
    if headers:
        hdrs.update(headers)
    connection = http.client.HTTPConnection(HOST, PORT, timeout=120)
    try:
        connection.request(method, path, body, hdrs)
        response = connection.getresponse()
        data = response.read()
        got = dict((k.lower(), v) for k, v in response.getheaders())
        return response.status, response.reason, got, data
    except Exception as ex:  # a closed connection, a timeout
        return 0, 'EXC ' + type(ex).__name__, {}, b''
    finally:
        connection.close()


def shown_headers(headers):
    return '; '.join('%s=%s' % (k, v) for k, v in sorted(headers.items()) if k not in DROP)


def ask(label, method, path, body=None, headers=None):
    status, reason, got, data = send(method, path, body, headers)
    text = data.decode('utf-8', 'replace')
    say(label, '%d %s\t%s\t%s' % (status, reason, shown_headers(got), text))
    return status, got, data


def call(label, function, rhs, nargout=1, mode='small', nan='string'):
    body = {'rhs': rhs, 'nargout': nargout, 'outputFormat': {'mode': mode, 'nanType': nan}}
    return ask(label, 'POST', '/%s/%s' % (COMPONENT, function), body)


# --- discovery and health -----------------------------------------------------------------------
ask('health', 'GET', '/api/health')
status, reason, headers, data = send('GET', '/api/discovery')
try:
    disc = json.loads(data)
    archives = disc.get('archives', {})
    names = sorted(archives.keys())
    say('discovery.archives', '%d %s' % (status, ' '.join(names)))
    arch = archives.get(COMPONENT, {})
    functions = arch.get('functions', {})
    say('discovery.functions', ' '.join(sorted(functions.keys())))
    say('discovery.release', str(arch.get('matlabRuntimeRelease')))
    add = functions.get('acc_add', {})
    say('discovery.acc_add', json.dumps(add, sort_keys=True, separators=(',', ':')))
    stats = functions.get('acc_stats', {})
    say('discovery.acc_stats', json.dumps(stats, sort_keys=True, separators=(',', ':')))
except ValueError:
    say('discovery.archives', '%d %s' % (status, data.decode('utf-8', 'replace')))

# --- synchronous calls ----------------------------------------------------------------------------
call('quiet', 'acc_quiet', [], nargout=0)
call('add', 'acc_add', [2, 3])
call('add.large', 'acc_add', [2, 3], mode='large')
call('add.nargout0', 'acc_add', [2, 3], nargout=0)
call('stats', 'acc_stats', [[3, 1, 4, 1, 5]])
call('stats.large', 'acc_stats', [[3, 1, 4, 1, 5]], mode='large')
call('greet', 'acc_greet', ['Ada'])
call('greet.large', 'acc_greet', [{'mwdata': ['Ada'], 'mwsize': [1, 3], 'mwtype': 'char'}], mode='large')
call('scale', 'acc_scale', [[[1, 2], [3, 4]], 10])
call('scale.large', 'acc_scale', [{'mwdata': [1, 3, 2, 4], 'mwsize': [2, 2], 'mwtype': 'double'}, 10], mode='large')
call('count.1', 'acc_count', [])
call('count.2', 'acc_count', [])
call('fail', 'acc_fail', ['from HTTP'], nargout=0)
call('add.wrongclass', 'acc_add', ['x', 3])
call('add.missing', 'acc_add', [2])
call('count.3', 'acc_count', [])
ask('nosuch', 'POST', '/%s/acc_nosuch' % COMPONENT, {'rhs': [1], 'nargout': 1})
ask('get', 'GET', '/%s/acc_add' % COMPONENT)

# --- the asynchronous protocol ----------------------------------------------------------------
status, reason, headers, data = send('POST', '/%s/acc_add?mode=async&client=acc' % COMPONENT, {'rhs': [20, 22], 'nargout': 1})
say('async.post', '%d\t%s\t%s' % (status, shown_headers(headers), data.decode('utf-8', 'replace')))
try:
    info = json.loads(data)
except ValueError:
    info = {}
if status == 201 and isinstance(info, dict) and 'self' in info:
    self_url = info['self']
    state = None
    for _ in range(60):
        s, r, h, d = send('GET', self_url + '/info')
        try:
            state = json.loads(d).get('state')
        except ValueError:
            state = None
        if state in ('READY', 'ERROR', 'CANCELLED'):
            break
        time.sleep(0.25)
    say('async.state', str(state))
    ask('async.info', 'GET', self_url + '/info')
    ask('async.result', 'GET', self_url + '/result')
    ask('async.collection', 'GET', info.get('up', '/~acc/requests'))
    ask('async.delete', 'DELETE', self_url)
    ask('async.deleted', 'GET', self_url + '/info')
    status2, reason2, headers2, data2 = send('POST', '/%s/acc_fail?mode=async&client=acc' % COMPONENT, {'rhs': ['async'], 'nargout': 0})
    try:
        info2 = json.loads(data2)
        url2 = info2['self']
        for _ in range(60):
            s, r, h, d = send('GET', url2 + '/info')
            st = json.loads(d).get('state')
            if st in ('READY', 'ERROR', 'CANCELLED'):
                break
            time.sleep(0.25)
        say('async.fail.state', str(st))
        ask('async.fail.result', 'GET', url2 + '/result')
        ask('async.fail.delete', 'DELETE', url2)
    except (ValueError, KeyError):
        say('async.fail.post', '%d %s' % (status2, data2.decode('utf-8', 'replace')))
else:
    say('async.state', 'not started')
say('end', 'ok')
