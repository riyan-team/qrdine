"""Checks a running Dine In PC edition (DineIn.exe) end to end: the licence, one restaurant per PC, dine-in from a
guest's phone to the bill, many phones and tables at the same moment on the PC's own SQLite database, the tax office
in Practice, reports, and (with --after-restart) that everything is still there after the exe is started again.

  PC_BASE=http://127.0.0.1:5310  PC_LICENCE=<key for this PC>  PC_LICENCE_ENDED=<an ended key>  PC_STATE=<file>
  python pc_check.py                  first start: a fresh data folder
  python pc_check.py --after-restart  the same data folder, the exe started again

The licence keys come from the seller's tool (tools/QrDine.Licence). Nothing is printed that could sign in.
"""
import concurrent.futures, datetime, json, os, re, secrets, sys, time, traceback, urllib.error, urllib.request

BASE = os.environ.get('PC_BASE', 'http://127.0.0.1:5310')
STATE_FILE = os.environ.get('PC_STATE', 'pc_check_state.json')
AFTER = '--after-restart' in sys.argv
passed, fails, current, state = 0, [], '-', {}


def raw_req(method, path, body=None, token=None, accept_json=True):
    data, headers = None, {'Accept': 'application/json'} if accept_json else {}
    if body is not None or method in ('POST', 'PUT'):
        data, headers['Content-Type'] = json.dumps(body if body is not None else {}).encode(), 'application/json'
    if token:
        headers['Authorization'] = 'Bearer ' + token
    rq = urllib.request.Request(BASE + path, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(rq, timeout=60) as resp:
            status, text, ctype = resp.status, resp.read().decode('utf-8', 'replace'), resp.headers.get('content-type', '')
    except urllib.error.HTTPError as e:
        status, text, ctype = e.code, e.read().decode('utf-8', 'replace'), e.headers.get('content-type', '')
    try:
        return status, (json.loads(text) if text and 'json' in ctype else text)
    except ValueError:
        return status, text


def fail(label, detail):
    fails.append(f'[{current}] {label}: {detail}')


def ok():
    global passed
    passed += 1


def step(label, method, path, body=None, token=None, expect=200):
    status, parsed = raw_req(method, path, body, token)
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


def r(path):
    return f"/api/r/{state['slug']}{path}"


T = lambda: state['token']


def line(item, qty=1):
    return {'menuItemId': item, 'qty': qty, 'note': None}


def walk(order_id):
    for s, body in (('approve', None), ('cook', {'etaMinutes': 10}), ('ready', None), ('serve', None)):
        step(s, 'POST', r(f'/orders/{order_id}/{s}'), body, T())


def settle(code, method='Cash'):
    return step(f'settle {code}', 'POST', r(f'/tables/{code}/settle'), {'method': method, 'discount': 0, 'amountTendered': 0}, T())


def run_all(fns):
    with concurrent.futures.ThreadPoolExecutor(len(fns)) as ex:
        return list(ex.map(lambda f: f(), fns))


# ------------------------------------------------------------------ first start
def pages():
    s, html = raw_req('GET', '/', accept_json=False)
    same('the web pages come from inside the exe', s, 200)
    same('it is the app', '<div id="root">' in str(html), True)
    js = re.search(r'/assets/index-[A-Za-z0-9_-]+\.js', str(html))
    same('its script is there too', raw_req('GET', js.group(0), accept_json=False)[0] if js else None, 200)
    same('health', raw_req('GET', '/health', accept_json=False)[0], 200)


def no_licence():
    l = step('licence', 'GET', '/api/licence')
    same('PC edition', l['edition'], 'pc')
    same('not activated yet', l['state'], 'Missing')
    same('no restaurant yet', l['hasRestaurant'], False)
    same("this PC's code is four groups of four", bool(re.fullmatch(r'[A-Z2-9]{4}(-[A-Z2-9]{4}){3}', l['machineCode'])), True)
    state['machine'] = l['machineCode']
    step('no sign-up before activation', 'POST', '/api/onboard', {'restaurantName': 'PC Grill'}, expect=402)
    step('nothing else either', 'GET', '/api/plans', expect=402)
    step('a made-up key is refused', 'POST', '/api/licence', {'key': 'DINEIN1.abc.def'}, expect=400)
    step('an ended licence is refused', 'POST', '/api/licence', {'key': os.environ['PC_LICENCE_ENDED']}, expect=400)
    same('still not activated', step('licence', 'GET', '/api/licence')['state'], 'Missing')
    l = step('activate', 'POST', '/api/licence', {'key': os.environ['PC_LICENCE']})
    same('activated', (l['canWork'], l['state']), (True, 'Active'))
    same('with the tax office', 'fbr' in l['features'], True)


def signup():
    mobile = '03' + ''.join(secrets.choice('0123456789') for _ in range(9))
    pw = secrets.token_urlsafe(14)
    plans = step('plans', 'GET', '/api/plans')
    me = step('sign up', 'POST', '/api/onboard', {'restaurantName': 'PC Grill', 'slug': None, 'ownerDisplayName': 'PC Owner',
                                                  'ownerUsername': mobile, 'ownerPassword': pw, 'planId': plans[-1]['id']})
    state.update(slug=me['slug'], token=me['token'], mobile=mobile, pw=pw)
    step('a second restaurant is refused', 'POST', '/api/onboard', {'restaurantName': 'Other Grill', 'slug': None, 'ownerDisplayName': 'X',
                                                                    'ownerUsername': '03' + '1' * 9, 'ownerPassword': pw, 'planId': plans[0]['id']}, expect=403)
    same('the licence page knows', step('licence', 'GET', '/api/licence')['hasRestaurant'], True)
    s = step('tax office settings', 'GET', r('/admin/fbr'), token=T())
    same('the licence put the tax office in the plan', s['featureFbr'], True)
    same('branches pick Practice / Test / Live themselves', s['forcedPractice'], False)
    same('FBR on this computer is possible here', s.get('localAvailable'), True)


def menu():
    c = step('category', 'POST', r('/admin/categories'), {'name': 'Mains', 'sortOrder': 1}, T())
    item = lambda name, price: step(f'item {name}', 'POST', r('/admin/items'), {
        'categoryId': c['id'], 'name': name, 'nameUrdu': '', 'description': '', 'price': price, 'emoji': '', 'available': True,
        'prepMinutes': 5, 'variants': None, 'modifierGroups': None, 'pctCode': '9801.0000'}, T())['id']
    state['K'], state['N'] = item('Karahi', 1200), item('Naan', 40)
    step('bill settings', 'PUT', r('/admin/billing-settings'), {
        'taxEnabled': True, 'taxName': 'GST', 'taxPercent': 16, 'serviceChargePercent': 0, 'discountEnabled': True, 'discountIsPercent': False,
        'discountValue': 0, 'receiptEnabled': True, 'stockTrackingEnabled': False, 'taxPercentCard': 5, 'serviceChargeTaxable': False,
        'oneTaxForAllItems': False}, T())
    step('filing on (Practice)', 'PUT', r('/admin/fbr'), {'fbrEnabled': True, 'businessName': 'PC Grill', 'ntn': '1234567-8', 'strn': '',
                                                          'sendAtSettle': True, 'autoRetry': True}, T())
    branch = step('branches', 'GET', r('/admin/branches'), token=T())[0]['id']
    have = {t['code'] for t in step('tables', 'GET', r('/tables'), token=T())}
    for n in range(1, 11):
        if f'T{n}' not in have:
            step(f'add table T{n}', 'POST', r('/admin/tables'), {'code': f'T{n}', 'name': f'Table {n}', 'branchId': branch}, T())
    state['keys'] = {t['code']: t['qrKey'] for t in step('tables', 'GET', r('/tables'), token=T())}


def guest_to_bill():
    g = step('guest scans T1', 'POST', r('/tables/T1/session'), {'key': state['keys']['T1']})['token']
    o = step('guest orders', 'POST', r('/orders'), {'tableCode': 'T1', 'items': [line(state['K']), line(state['N'], 2)], 'note': None}, g)
    walk(o['id'])
    inv = settle('T1')
    same('bill total with 16% tax', inv['total'], round((1200 + 80) * 1.16, 2))
    same('filed in Practice', (inv['fiscalStatus'], inv['fiscalEnvironment']), ('Registered', 'Practice'))
    same('with a practice number', str(inv['fiscalNumber']).startswith('PRACTICE-'), True)
    state.setdefault('bills', []).append(inv['number'])
    state['total'] = state.get('total', 0) + inv['total']


def busy_moment():
    keys = state['keys']
    g = step('guest scans T2', 'POST', r('/tables/T2/session'), {'key': keys['T2']})['token']
    res = run_all([(lambda: raw_req('POST', r('/orders'), {'tableCode': 'T2', 'items': [line(state['N'])], 'note': None}, g)) for _ in range(10)])
    same('ten phones at one table, all accepted', [s for s, _ in res if s != 200], [])
    same('ten different orders', len({p['id'] for s, p in res if s == 200}), 10)
    codes = [f'T{i}' for i in range(3, 11)]
    for c in codes:
        walk(step(f'staff order {c}', 'POST', r('/orders'), {'tableCode': c, 'items': [line(state['K'])], 'note': None}, T())['id'])
    res = run_all([(lambda c=c: raw_req('POST', r(f'/tables/{c}/settle'), {'method': 'Cash', 'discount': 0, 'amountTendered': 0}, T())) for c in codes])
    good = [p for s, p in res if s == 200]
    same('eight tables settled at the same moment: eight bills', len(good), 8)
    same('no server errors', [s for s, _ in res if s >= 500], [])
    same('every bill number different', len({p['number'] for p in good}), 8)
    res = run_all([(lambda: raw_req('POST', r('/tables/T2/settle'), {'method': 'Cash', 'discount': 0, 'amountTendered': 0}, T())) for _ in range(4)])
    same('four cashiers settle T2 together: one bill', len([1 for s, _ in res if s == 200]), 1)
    for s, p in res:
        if s == 200:
            good.append(p)
    state['bills'] += [p['number'] for p in good]
    state['total'] += sum(p['total'] for p in good)


def reports():
    today = (datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(hours=5)).date().isoformat()
    s = step('sales report', 'GET', r(f'/admin/reports/sales?from={today}&to={today}&page=1&pageSize=100'), token=T())
    rows = s['rows'] if isinstance(s, dict) else s
    same('every bill is in the report', len(rows), len(state['bills']))
    same('and the money adds up', round(sum(x['total'] for x in rows), 2), round(state['total'], 2))
    with open(STATE_FILE, 'w') as f:
        json.dump({k: state[k] for k in ('slug', 'mobile', 'pw', 'bills', 'total', 'machine')}, f)


# ------------------------------------------------------------------ after a restart
def restarted():
    state.update(json.load(open(STATE_FILE)))
    l = step('licence', 'GET', '/api/licence')
    same('still activated after a restart', (l['state'], l['machineCode']), ('Active', state['machine']))
    me = step('owner signs in', 'POST', '/api/auth/login', {'username': state['mobile'], 'password': state['pw']})
    state['token'] = me['token']
    today = (datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(hours=5)).date().isoformat()
    s = step('sales report', 'GET', r(f'/admin/reports/sales?from={today}&to={today}&page=1&pageSize=100'), token=T())
    rows = s['rows'] if isinstance(s, dict) else s
    same('every bill is still there', sorted(x['number'] for x in rows), sorted(state['bills']))
    step('the menu is still there', 'GET', f"/api/r/{state['slug']}", expect=200)


if AFTER:
    section('after a restart', restarted)
else:
    section('the web pages', pages)
    section('licence: none, wrong, ended, then a real one', no_licence)
    section('one restaurant per PC, features from the licence', signup)
    section('menu, bill settings, tables', menu)
    section('a guest phone to the bill, filed in Practice', guest_to_bill)
    section('many phones and tables at once on SQLite', busy_moment)
    section('reports add up', reports)

print(f'\nPASSED {passed}, FAILED {len(fails)}')
for f in fails:
    print(' - ' + f)
