"""Hybrid end to end with real processes (run by run-hybrid-check.sh): an online server and a DineIn.exe.

  HY_SERVER=http://127.0.0.1:5331  HY_PC=http://127.0.0.1:5321  HY_LICENCE=<key for the PC>  HY_STATE=<file>
  python hybrid_check.py connect   the owner signs up online, pairs the PC; the PC downloads, takes over and sells
  python hybrid_check.py offline   the server is stopped: the PC keeps selling and says it is offline
  python hybrid_check.py back      the server is back: the PC catches up by itself

Nothing is printed that could sign in (passwords, tokens, the PC key, the licence key).
"""
import datetime, json, os, secrets, sys, time, traceback, urllib.error, urllib.request

SERVER = os.environ.get('HY_SERVER', 'http://127.0.0.1:5331')
PC = os.environ.get('HY_PC', 'http://127.0.0.1:5321')
STATE_FILE = os.environ.get('HY_STATE', 'hybrid_state.json')
PHASE = sys.argv[1] if len(sys.argv) > 1 else 'connect'
passed, fails, current, state = 0, [], '-', {}


def req(base, method, path, body=None, token=None):
    data, headers = None, {'Accept': 'application/json'}
    if body is not None or method in ('POST', 'PUT'):
        data, headers['Content-Type'] = json.dumps(body if body is not None else {}).encode(), 'application/json'
    if token:
        headers['Authorization'] = 'Bearer ' + token
    rq = urllib.request.Request(base + path, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(rq, timeout=60) as resp:
            status, text, ctype = resp.status, resp.read().decode('utf-8', 'replace'), resp.headers.get('content-type', '')
    except urllib.error.HTTPError as e:
        status, text, ctype = e.code, e.read().decode('utf-8', 'replace'), e.headers.get('content-type', '')
    except (urllib.error.URLError, ConnectionError, TimeoutError) as e:
        return 0, str(e)
    try:
        return status, (json.loads(text) if text and 'json' in ctype else text)
    except ValueError:
        return status, text


def fail(label, detail):
    fails.append(f'[{current}] {label}: {detail}')


def ok():
    global passed
    passed += 1


def step(label, base, method, path, body=None, token=None, expect=200):
    status, parsed = req(base, method, path, body, token)
    if expect == 200 and method in ('POST', 'PUT', 'DELETE'):
        expect = [200, 204]
    good = status in expect if isinstance(expect, (list, tuple, set)) else status == expect
    ok() if good else fail(label, f'{method} {path} gave {status}, wanted {expect}: {str(parsed)[:300]}')
    return parsed


def same(label, actual, expected):
    num = isinstance(expected, (int, float)) and isinstance(actual, (int, float)) and not isinstance(expected, bool)
    good = abs(actual - expected) < 0.011 if num else actual == expected
    ok() if good else fail(label, f'got {actual!r}, wanted {expected!r}')


def section(name, fn):
    global current
    current = name
    before = len(fails)
    try:
        fn()
    except Exception as e:
        fail('stopped early', f'{type(e).__name__}: {e} @ {traceback.extract_tb(e.__traceback__)[-1].lineno}')
    print(f"{'FAIL' if len(fails) > before else 'ok  '}  {name}", flush=True)


def until(label, check, seconds=60):
    """Waits for check() to give something truthy; the PC syncs every few seconds."""
    end, last = time.time() + seconds, None
    while time.time() < end:
        last = check()
        if last:
            ok()
            return last
        time.sleep(1)
    fail(label, f'not within {seconds} s (last: {str(last)[:200]})')
    return last


def r(path):
    return f"/api/r/{state['slug']}{path}"


def line(item, qty=1):
    return {'menuItemId': item, 'qty': qty, 'note': None}


def sell(base, token, code, items):
    """A guest at the table orders, the kitchen walks it, the counter settles it."""
    key = state['keys'][code]
    g = step(f'guest scans {code}', base, 'POST', r(f'/tables/{code}/session'), {'key': key})['token']
    o = step(f'guest orders at {code}', base, 'POST', r('/orders'), {'tableCode': code, 'items': items, 'note': None}, g)
    for s, body in (('approve', None), ('cook', {'etaMinutes': 10}), ('ready', None), ('serve', None)):
        step(s, base, 'POST', r(f"/orders/{o['id']}/{s}"), body, token)
    return step(f'settle {code}', base, 'POST', r(f'/tables/{code}/settle'), {'method': 'Cash', 'discount': 0, 'amountTendered': 0}, token)


def report(base, token):
    today = (datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(hours=5)).date().isoformat()
    s = req(base, 'GET', r(f'/admin/reports/sales?from={today}&to={today}&page=1&pageSize=100'), token=token)[1]
    rows = s.get('rows', []) if isinstance(s, dict) else (s if isinstance(s, list) else [])
    return sorted((x['number'], round(x['total'], 2)) for x in rows)


def item_price(base, name):
    s, m = req(base, 'GET', f"/api/r/{state['slug']}")
    for c in (m.get('menu') or []) if isinstance(m, dict) else []:
        for i in c.get('items', []):
            if i['name'] == name:
                return i['price']
    return None


def save():
    with open(STATE_FILE, 'w') as f:
        json.dump(state, f)


def pc_status():
    return req(PC, 'GET', '/api/pc/sync')[1]


# ------------------------------------------------------------------ 1. connect
def online_restaurant():
    state['mobile'] = '03' + ''.join(secrets.choice('0123456789') for _ in range(9))
    state['pw'] = secrets.token_urlsafe(14)
    plans = step('plans', SERVER, 'GET', '/api/plans')
    me = step('the owner signs up online', SERVER, 'POST', '/api/onboard', {
        'restaurantName': 'Hybrid Grill', 'slug': None, 'ownerDisplayName': 'Hybrid Owner',
        'ownerUsername': state['mobile'], 'ownerPassword': state['pw'], 'planId': plans[-1]['id']})
    state['slug'], T = me['slug'], me['token']
    state['server_token'] = T
    c = step('category', SERVER, 'POST', r('/admin/categories'), {'name': 'Mains', 'sortOrder': 1}, T)
    def item(name, price):
        return step(f'item {name}', SERVER, 'POST', r('/admin/items'), {
            'categoryId': c['id'], 'name': name, 'nameUrdu': '', 'description': '', 'price': price, 'emoji': '', 'available': True,
            'prepMinutes': 5, 'variants': None, 'modifierGroups': None, 'pctCode': '9801.0000'}, T)['id']
    state['K'], state['N'], state['cat'] = item('Karahi', 1200), item('Naan', 40), c['id']
    step('bill settings: 16% tax', SERVER, 'PUT', r('/admin/billing-settings'), {
        'taxEnabled': True, 'taxName': 'GST', 'taxPercent': 16, 'serviceChargePercent': 0, 'discountEnabled': True, 'discountIsPercent': False,
        'discountValue': 0, 'receiptEnabled': True, 'stockTrackingEnabled': False, 'taxPercentCard': 16, 'serviceChargeTaxable': False,
        'oneTaxForAllItems': True}, T)
    branch = step('branches', SERVER, 'GET', r('/admin/branches'), token=T)[0]['id']
    have = {t['code'] for t in step('tables', SERVER, 'GET', r('/tables'), token=T)}
    for n in range(1, 5):
        if f'T{n}' not in have:
            step(f'add table T{n}', SERVER, 'POST', r('/admin/tables'), {'code': f'T{n}', 'name': f'Table {n}', 'branchId': branch}, T)
    state['keys'] = {t['code']: t['qrKey'] for t in step('tables', SERVER, 'GET', r('/tables'), token=T)}
    inv = sell(SERVER, T, 'T1', [line(state['K']), line(state['N'], 2)])
    same('bill #1 online', (inv['number'], inv['total']), ('INV-00001', round(1280 * 1.16, 2)))
    state['bills'] = [(inv['number'], round(inv['total'], 2))]


def pair_and_connect():
    T = state['server_token']
    pc = step('the owner pairs a PC', SERVER, 'POST', r('/admin/sync/devices'), {'name': 'Counter PC'}, T)
    step('a second PC is refused', SERVER, 'POST', r('/admin/sync/devices'), {'name': 'Second PC'}, T, expect=400)
    l = step('activate the PC', PC, 'POST', '/api/licence', {'key': os.environ['HY_LICENCE']})
    same('activated', l['canWork'], True)
    same('nothing here yet', (l['hasRestaurant'], pc_status()['state']), (False, 'off'))
    bad = step('a made-up PC key is refused', PC, 'POST', '/api/pc/sync/connect', {'server': SERVER, 'key': 'dk_' + secrets.token_hex(16)}, expect=400)
    same('and it says why', "isn't paired" in str(bad), True)
    same('still nothing here', pc_status()['state'], 'off')
    st = step('connect', PC, 'POST', '/api/pc/sync/connect', {'server': SERVER, 'key': pc['key']})
    same('downloading', st['state'] in ('connecting', 'ready'), True)
    if pc_status()['state'] == 'connecting':
        same('nothing else opens while it downloads', req(PC, 'GET', '/api/plans')[0], 503)
    st = until('the download finishes', lambda: (lambda s: s if s and s['state'] == 'ready' else None)(pc_status()), 120)
    same('connected to the right restaurant', (st or {}).get('restaurant'), 'Hybrid Grill')
    devices = step('devices', SERVER, 'GET', r('/admin/sync/devices'), token=T)
    same('the server knows the counter is on the PC', bool(devices[0].get('counterSince')), True)
    step('a connected PC takes a new key only from a signed-in owner or manager', PC, 'POST', '/api/pc/sync/connect',
         {'server': SERVER, 'key': pc['key']}, expect=403)


def pc_has_it_all():
    me = step('the owner signs in on the PC with the online password', PC, 'POST', '/api/auth/login', {'username': state['mobile'], 'password': state['pw']})
    state['pc_token'] = me['token']
    same('same restaurant address', me['slug'], state['slug'])
    same('the menu came down', (item_price(PC, 'Karahi'), item_price(PC, 'Naan')), (1200, 40))
    same("yesterday's bill came down", report(PC, state['pc_token']), state['bills'])
    keys = {t['code']: t['qrKey'] for t in step('tables', PC, 'GET', r('/tables'), token=state['pc_token'])}
    same('the tables came down', sorted(keys), ['T1', 'T2', 'T3', 'T4'])
    state['keys'] = keys


def server_steps_back():
    T = state['server_token']
    no = step('the server no longer takes orders', SERVER, 'POST', r('/orders'), {'tableCode': 'T2', 'items': [line(state['K'])], 'note': None}, T, expect=400)
    same('and says where they are made', 'runs on its own PC' in str(no), True)
    step('the owner changes a price from home', SERVER, 'PUT', r(f"/admin/items/{state['K']}"), {
        'categoryId': state['cat'], 'name': 'Karahi', 'nameUrdu': '', 'description': '', 'price': 1300, 'emoji': '', 'available': True,
        'prepMinutes': 5, 'variants': None, 'modifierGroups': None, 'pctCode': '9801.0000'}, T)
    until('the new price reaches the PC', lambda: item_price(PC, 'Karahi') == 1300, 30)


def pc_sells():
    inv = sell(PC, state['pc_token'], 'T2', [line(state['K'])])
    same('bill #2 on the PC, at the new price', (inv['number'], inv['total']), ('INV-00002', round(1300 * 1.16, 2)))
    state['bills'].append((inv['number'], round(inv['total'], 2)))
    until('the bill reaches the online copy', lambda: report(SERVER, state['server_token']) == state['bills'], 30)
    st = until('nothing waiting on the PC', lambda: (lambda s: s if s and s['online'] and s['waiting'] == 0 else None)(pc_status()), 30)
    same('no changes refused online', (st or {}).get('serverFailed'), 0)
    save()


# ------------------------------------------------------------------ 2. offline
def offline():
    state.update(json.load(open(STATE_FILE)))
    inv = sell(PC, state['pc_token'], 'T3', [line(state['N'], 5)])
    same('bill #3 with no internet', (inv['number'], inv['total']), ('INV-00003', round(200 * 1.16, 2)))
    state['bills'].append((inv['number'], round(inv['total'], 2)))
    st = until('the PC says it is offline', lambda: (lambda s: s if s and not s['online'] else None)(pc_status()), 30)
    same('and that it keeps working', 'keeps working' in str((st or {}).get('lastError')), True)
    same('with the bill waiting to go up', ((st or {}).get('waiting') or 0) > 0, True)
    save()


# ------------------------------------------------------------------ 3. back
def back():
    state.update(json.load(open(STATE_FILE)))
    state['bills'] = [tuple(b) for b in state['bills']]
    until('the PC catches up by itself', lambda: report(SERVER, state['server_token']) == state['bills'], 60)
    st = until('nothing waiting', lambda: (lambda s: s if s and s['online'] and s['waiting'] == 0 else None)(pc_status()), 30)
    same('no changes refused online', (st or {}).get('serverFailed'), 0)
    same('both sides have the same bills', report(PC, state['pc_token']), report(SERVER, state['server_token']))
    devices = step('devices', SERVER, 'GET', r('/admin/sync/devices'), token=state['server_token'])
    same('nothing failed online', (devices[0]['failed'], devices[0]['waiting']), (0, 0))


if PHASE == 'connect':
    section('online: sign up, menu, tables, one bill', online_restaurant)
    section('pair the PC, connect, download, take over', pair_and_connect)
    section('the PC has it all, and the owner signs in there', pc_has_it_all)
    section('the server steps back; menu edits from home still reach the PC', server_steps_back)
    section('the PC sells and the online copy follows', pc_sells)
elif PHASE == 'offline':
    section('no internet: the PC keeps selling', offline)
else:
    section('internet back: the PC catches up', back)

print(f'\nPASSED {passed}, FAILED {len(fails)}')
for f in fails:
    print(' - ' + f)
sys.exit(1 if fails else 0)
