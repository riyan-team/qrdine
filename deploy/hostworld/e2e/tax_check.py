#!/usr/bin/env python3
"""Files real bills through the app against a stand-in tax office (mock_tax_office.py), never the real FBR or PRA.

Run it against a throwaway copy of the app started with:
  Fiscal__Simulate=false            (otherwise every branch is held in Practice)
  Fiscal__AutoRetryMinutes=1        (so the automatic resend is seen within a few minutes)
  Fiscal__CloudSandboxUrl / Fiscal__CloudProductionUrl / Fiscal__PraSandboxUrl / Fiscal__PraProductionUrl
                                    pointing at http://127.0.0.1:<port>/fbr-test, /fbr-live, /pra-test, /pra-live
Live steps run only with TAX_CHECK_LIVE_IS_MOCK=1, the promise that Live goes to the stand-in too.
Covers: Practice / Test / Live, FBR and PRA formats, the sums, wrong token, outage and resend (one, picked, all),
refusal and resend, never filing twice (parallel sends), send-later, the automatic resend, one tax for all items.
Uses generated test values only and prints no passwords or tokens."""
import concurrent.futures, datetime, json, os, random, re, secrets, sys, time, traceback, urllib.error, urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mock_tax_office  # noqa: E402

BASE = os.environ.get('QRDINE_E2E_BASE', 'http://127.0.0.1:5199')
ENV_FILE = os.environ.get('QRDINE_E2E_ENV', '/run/qrdine-e2e.env')
PORT = int(os.environ.get('MOCK_TAX_PORT', '8599'))
LIVE_IS_MOCK = os.environ.get('TAX_CHECK_LIVE_IS_MOCK') == '1'
MOCK = f'http://127.0.0.1:{PORT}'
TOKEN = 'mock-' + secrets.token_hex(8)
passed, fails, current, state = 0, [], '-', {}


def raw_req(method, path, body=None, token=None, base=BASE):
    data, headers = None, {}
    if body is not None or method in ('POST', 'PUT'):
        data, headers['Content-Type'] = json.dumps(body if body is not None else {}).encode(), 'application/json'
    if token:
        headers['Authorization'] = 'Bearer ' + token
    for _ in range(6):
        rq = urllib.request.Request(base + path, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(rq, timeout=45) as resp:
                status, text = resp.status, resp.read().decode('utf-8', 'replace')
        except urllib.error.HTTPError as e:
            status, text = e.code, e.read().decode('utf-8', 'replace')
        if status == 429:
            time.sleep(15)
            continue
        break
    try:
        return status, json.loads(text) if text else None
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
    good = abs(actual - expected) < 0.011 if isinstance(expected, (int, float)) and isinstance(actual, (int, float)) and not isinstance(expected, bool) else actual == expected
    ok() if good else fail(label, f'got {actual!r}, wanted {expected!r}')


def has(label, text, part):
    good = part.lower() in str(text or '').lower()
    ok() if good else fail(label, f'{part!r} not in {str(text)[:300]!r}')


def section(name, fn):
    global current
    current = name
    before = len(fails)
    try:
        fn()
    except Exception as e:
        fail('stopped early', f'{type(e).__name__}: {e} @ {traceback.extract_tb(e.__traceback__)[-1].lineno}')
        # Put things back so one broken section doesn't make the next ones fail too.
        try:
            mock('POST', '/control', {'mode': 'ok'})
            if state.get('branch'):
                filing()
        except Exception:
            pass
    print(f"{'FAIL' if len(fails) > before else 'ok  '}  {name}", flush=True)


def mock(method, path, body=None):
    return raw_req(method, path, body, base=MOCK)[1]


def r(path):
    return f"/api/r/{state['slug']}{path}"


T = lambda: state['token']


def line(item, qty=1):
    return {'menuItemId': item, 'qty': qty, 'note': None}


def bill_on(code, *lines, method='Cash', discount=0):
    o = step(f'order {code}', 'POST', r('/orders'), {'tableCode': code, 'items': list(lines), 'note': None}, T())
    for s, body in (('approve', None), ('cook', {'etaMinutes': 10}), ('ready', None), ('serve', None)):
        step(s, 'POST', r(f"/orders/{o['id']}/{s}"), body, T())
    return step(f'settle {code}', 'POST', r(f'/tables/{code}/settle'), {'method': method, 'discount': discount, 'amountTendered': 0}, T())


def billing(**kw):
    body = {'taxEnabled': True, 'taxName': 'GST', 'taxPercent': 16, 'serviceChargePercent': 10, 'discountEnabled': True,
            'discountIsPercent': False, 'discountValue': 0, 'receiptEnabled': True, 'stockTrackingEnabled': False,
            'taxPercentCard': 5, 'serviceChargeTaxable': True, 'oneTaxForAllItems': False}
    body.update(kw)
    step('bill settings', 'PUT', r('/admin/billing-settings'), body, T())


def filing(**kw):
    body = {'fbrEnabled': True, 'businessName': 'E2E Tax Grill', 'ntn': '1234567-8', 'strn': '17-00-1234-567-89',
            'sendAtSettle': True, 'autoRetry': False}
    body.update(kw)
    step('tax office settings', 'PUT', r('/admin/fbr'), body, T())


def branch(expect=200, **kw):
    body = {'name': 'Main Branch', 'address': '', 'isActive': True, 'fiscalAuthority': 'FBR', 'fiscalEnvironment': 'Test',
            'fbrMode': 'Cloud', 'fbrSandbox': True, 'fbrPosId': 110014, 'fbrToken': None, 'fbrStaticIp': '203.0.113.10',
            'fbrEndpointOverride': None}
    body.update(kw)
    return step(f"branch: {body['fiscalAuthority']} {body['fiscalEnvironment']}", 'PUT', r(f"/admin/branches/{state['branch']}"), body, T(),
                expect=expect if expect != 200 else [200, 204])


def pending():
    return step('waiting list', 'GET', r('/admin/fbr/invoices/pending'), token=T())


def invoice(i):
    return step('receipt', 'GET', r(f"/invoices/{i['id']}"), token=T())


# ------------------------------------------------------------------ setup
def setup():
    srv = mock_tax_office.start(PORT, TOKEN)
    state['srv'] = srv
    mobile = '03' + ''.join(random.choices('0123456789', k=9))
    me = step('sign up', 'POST', '/api/onboard', {'restaurantName': 'E2E Tax Grill', 'slug': None, 'ownerDisplayName': 'Tax Owner',
                                                   'ownerUsername': mobile, 'ownerPassword': secrets.token_urlsafe(16), 'planId': 4})
    state['slug'], state['token'] = me['slug'], me['token']
    env = dict(l.strip().split('=', 1) for l in open(ENV_FILE, encoding='utf-8') if '=' in l and not l.startswith('#'))
    op = step('operator signs in', 'POST', '/api/platform/login', {'username': env['PlatformAdmin__Username'], 'password': env['PlatformAdmin__Password']})['token']
    rows = step('restaurants', 'GET', '/api/platform/restaurants', token=op)
    rows = rows if isinstance(rows, list) else rows.get('rows', [])
    rid = next(x for x in rows if x['slug'] == state['slug'])['id']
    s = step('filing off before the operator turns it on', 'GET', r('/admin/fbr'), token=T())
    same('not in the plan yet', s['featureFbr'], False)
    step('operator puts filing in the plan', 'POST', f'/api/platform/restaurants/{rid}/fbr', {'enabled': True}, op)
    s = step('tax office settings', 'GET', r('/admin/fbr'), token=T())
    same('in the plan', s['featureFbr'], True)
    same('server is not forcing practice (Fiscal__Simulate=false)', s['forcedPractice'], False)
    same('send at settle is on by default', s['sendAtSettle'], True)
    same('automatic resend is on by default', s['autoRetry'], True)
    filing()
    c = step('category', 'POST', r('/admin/categories'), {'name': 'Tax Test', 'sortOrder': 1}, T())
    item = lambda name, price, pct, **kw: step(f'item {name}', 'POST', r('/admin/items'), {
        'categoryId': c['id'], 'name': name, 'nameUrdu': '', 'description': '', 'price': price, 'emoji': '', 'available': True,
        'prepMinutes': 5, 'variants': None, 'modifierGroups': None, 'pctCode': pct, **kw}, T())['id']
    state['K'] = item('Tax Karahi', 1200, '9801.0000')
    state['N'] = item('Tax Naan', 40, '1905.9090')
    state['W'] = item('Tax Water', 60, '2201.1010', taxRate=0)        # tax-free
    state['C'] = item('Tax Cola', 150, '2202.1010', taxRate=18)       # its own rate
    billing()
    state['branch'] = step('branches', 'GET', r('/admin/branches'), token=T())[0]['id']
    have = {t['code'] for t in step('tables', 'GET', r('/tables'), token=T())}
    for n in range(1, 11):
        if f'T{n}' not in have:
            step(f'add table T{n}', 'POST', r('/admin/tables'), {'code': f'T{n}', 'name': f'Table {n}', 'branchId': state['branch']}, T())


# ------------------------------------------------------------------ practice
def practice():
    b = step('branches', 'GET', r('/admin/branches'), token=T())[0]
    same('a new branch starts in Practice', b['fiscalEnvironment'], 'Practice')
    same('and with FBR', b['fiscalAuthority'], 'FBR')
    chk = step('check setup', 'GET', r('/admin/fbr/check'), token=T())[0]
    same('practice is ready', chk['ready'], True)
    before = mock('GET', '/stats')['requests']
    inv = bill_on('T1', line(state['K']))
    same('practice bill is filed', inv['fiscalStatus'], 'Registered')
    same('with a practice number', str(inv['fiscalNumber']).startswith('PRACTICE-'), True)
    same('marked Practice', inv['fiscalEnvironment'], 'Practice')
    same('nothing reached the tax office', mock('GET', '/stats')['requests'], before)


# ------------------------------------------------------------------ settings rules
def rules():
    r1 = branch(expect=400, fiscalEnvironment='Live', fbrPosId=None, fbrToken='')
    has('Live needs a POSID', r1, 'POSID')
    r2 = branch(expect=400, fiscalEnvironment='Live', fbrToken='')
    has('Live needs a token', r2, 'token')
    branch(expect=400, fiscalEnvironment='Sometimes')
    branch(expect=400, fiscalAuthority='KPRA')
    branch(expect=400, fbrPosId=0)
    branch(expect=400, fbrEndpointOverride='ftp://example')
    branch(fbrToken=TOKEN, fbrEndpointOverride=None)
    b = step('branches', 'GET', r('/admin/branches'), token=T())[0]
    same('the token never comes back', b['fbrToken'], None)
    same('only that one is saved', b['hasFbrToken'], True)
    same('and its last 4', b['tokenEnds'], TOKEN[-4:])
    same('Test', b['fiscalEnvironment'], 'Test')
    step('name edit keeps the setup', 'PUT', r(f"/admin/branches/{state['branch']}"), {'name': 'Main Branch', 'address': 'Mall Road', 'isActive': True, 'keepFiscal': True}, T())
    b = step('branches', 'GET', r('/admin/branches'), token=T())[0]
    same('still Test after a name edit', (b['fiscalEnvironment'], b['hasFbrToken'], b['fbrPosId']), ('Test', True, 110014))
    chk = step('check setup', 'GET', r('/admin/fbr/check'), token=T())[0]
    same('Test with POSID and token is ready', (chk['ready'], chk['problems']), (True, []))
    branch(fbrMode='Local')
    chk = step('check setup', 'GET', r('/admin/fbr/check'), token=T())[0]
    same('the online system cannot use a PC\'s FBR software', chk['ready'], False)
    has('it says why', chk['problems'], 'PC edition')
    branch(fiscalAuthority='PRA', fbrMode='Local')
    same('PRA is online only', step('branches', 'GET', r('/admin/branches'), token=T())[0]['fbrMode'], 'Cloud')
    branch()


# ------------------------------------------------------------------ test bills and real bills on Test
def test_env():
    t = step('send a test bill', 'POST', r(f"/admin/fbr/branches/{state['branch']}/test"), token=T())
    same('test bill accepted', t['accepted'], True)
    same('by FBR Test', (t['office'], t['environment']), ('FBR', 'Test'))
    last = mock('GET', '/stats')['last']
    same('it went to FBR\'s test address', last['path'], '/fbr-test/PostData')
    same('with the POSID', last['body']['POSID'], 110014)
    same('nothing saved for a test bill', len(pending()), 0)

    branch(fbrToken='wrong-token-123')
    t = step('test bill with a wrong token', 'POST', r(f"/admin/fbr/branches/{state['branch']}/test"), token=T())
    same('refused', t['accepted'], False)
    has('in plain words', t['error'], "didn't accept the token")
    branch(fbrToken=TOKEN)

    inv = bill_on('T2', line(state['K'], 2), line(state['N'], 3), line(state['W']), line(state['C']), method='Card', discount=100)
    same('filed at settle', inv['fiscalStatus'], 'Registered')
    same('Test', inv['fiscalEnvironment'], 'Test')
    same('FBR', inv['fiscalOffice'], 'FBR')
    st = mock('GET', '/stats')
    body = st['last']['body']
    same("the office's number is on the bill", inv['fiscalNumber'], mock_tax_office.STATE['filed'].get(f"110014:{body['USIN']}"))
    same('the filed total is the bill total', body['TotalBillAmount'], inv['total'])
    same('card is payment mode 2', body['PaymentMode'], 2)
    same('FBR date format', bool(re.match(r'^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$', body['DateTime'])), True)
    rates = {it['ItemName']: it['TaxRate'] for it in body['Items']}
    same('item rates: card rate, tax-free, own rate', (rates.get('Tax Karahi'), rates.get('Tax Water'), rates.get('Tax Cola')), (5, 0, 18))
    same('the service charge is filed as its own line', 'Service Charge' in rates, True)
    rc = invoice(inv)
    same('receipt carries the NTN, STRN and POSID', (rc['sellerNtn'], rc['sellerStrn'], rc['posId']), ('1234567-8', '17-00-1234-567-89', 110014))
    step('a filed bill is never sent again', 'POST', r(f"/admin/fbr/invoices/{inv['id']}/retry"), token=T(), expect=400)


# ------------------------------------------------------------------ outage and resend
def outage():
    mock('POST', '/control', {'mode': 'down'})
    bills = [bill_on(f'T{n}', line(state['K'])) for n in (3, 4, 5, 6)]
    same('bills still settle and print while the office is down', all(b['fiscalStatus'] == 'Pending' for b in bills), True)
    waiting = pending()
    same('they wait', len(waiting), 4)
    has('with the reason', waiting[0]['fiscalError'], 'having a problem')
    res = step('send all, still down', 'POST', r('/admin/fbr/invoices/retry'), {'ids': None}, T())
    same('stops after 3 that could not reach the office', (res['sent'], res['stillWaiting']), (0, 3))
    has('and says so', res['stopped'], "couldn't be reached")
    mock('POST', '/control', {'mode': 'ok'})
    res = step('send picked', 'POST', r('/admin/fbr/invoices/retry'), {'ids': [bills[0]['id']]}, T())
    same('only the picked one', (res['sent'], res['left']), (1, 3))
    res = step('send all', 'POST', r('/admin/fbr/invoices/retry'), {'ids': None}, T())
    same('the rest go', (res['sent'], res['left']), (3, 0))


def refusal():
    mock('POST', '/control', {'mode': 'refuse'})
    inv = bill_on('T7', line(state['N'], 2))
    same('a refused bill is marked refused', inv['fiscalStatus'], 'Failed')
    has('with the office\'s reason', inv['fiscalError'], 'Invalid PCT code')
    mock('POST', '/control', {'mode': 'ok'})
    again = step('send it again once fixed', 'POST', r(f"/admin/fbr/invoices/{inv['id']}/retry"), token=T())
    same('filed', again['fiscalStatus'], 'Registered')


def never_twice():
    filing(sendAtSettle=False)
    inv = bill_on('T8', line(state['K']))
    same('send-later: the bill waits', inv['fiscalStatus'], 'Pending')
    has('as waiting to be sent', inv['fiscalError'], 'Waiting to be sent')
    before = mock('GET', '/stats')
    with concurrent.futures.ThreadPoolExecutor(8) as ex:
        results = list(ex.map(lambda _: raw_req('POST', r(f"/admin/fbr/invoices/{inv['id']}/retry"), None, T()), range(8)))
    after = mock('GET', '/stats')
    same('eight presses at once send it once', after['requests'] - before['requests'], 1)
    same('one filing', after['filed'] - before['filed'], 1)
    same('no USIN filed twice', after['duplicates'], 0)
    same('the others were told why', all(s == 200 or (s == 400 and ('being sent' in str(p) or 'already filed' in str(p))) for s, p in results), True)
    a, b = bill_on('T9', line(state['K'])), bill_on('T10', line(state['C']))
    same('both wait', (a['fiscalStatus'], b['fiscalStatus']), ('Pending', 'Pending'))
    res = step('send all waiting', 'POST', r('/admin/fbr/invoices/retry'), {'ids': None}, T())
    same('both sent', (res['sent'], res['left']), (2, 0))
    filing(sendAtSettle=True)


def pra():
    branch(fiscalAuthority='PRA', fbrPosId=220022)
    inv = bill_on('T1', line(state['K']), line(state['W']))
    same('PRA filed', (inv['fiscalStatus'], inv['fiscalOffice'], inv['fiscalEnvironment']), ('Registered', 'PRA', 'Test'))
    has('the receipt QR opens PRA\'s page', inv['fiscalQrPayload'], 'reg.pra.punjab.gov.pk')
    last = mock('GET', '/stats')['last']
    same('to PRA\'s test address', last['path'], '/pra-test/PostData')
    same('PRA field name', ('BuyerPNTN' in last['body'], 'BuyerNTN' in last['body']), (True, False))
    same('PRA date has milliseconds', bool(re.match(r'^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}$', last['body']['DateTime'])), True)
    t = step('PRA test bill', 'POST', r(f"/admin/fbr/branches/{state['branch']}/test"), token=T())
    same('accepted by PRA Test', (t['accepted'], t['office']), (True, 'PRA'))


def one_tax():
    billing(oneTaxForAllItems=True)
    o = step('order T3', 'POST', r('/orders'), {'tableCode': 'T3', 'items': [line(state['W']), line(state['C'])], 'note': None}, T())
    bill = step('bill', 'GET', r('/tables/T3/bill?method=Cash'), token=T())
    # water 60 + cola 150, service 10% = 21, all taxed at 16%: 210 + 21 + 33.60 + 3.36
    same('one rate on everything, the tax-free water too', bill['total'], 267.96)
    for s, body in (('approve', None), ('cook', {'etaMinutes': 10}), ('ready', None), ('serve', None)):
        step(s, 'POST', r(f"/orders/{o['id']}/{s}"), body, T())
    inv = step('settle T3', 'POST', r('/tables/T3/settle'), {'method': 'Cash', 'discount': 0, 'amountTendered': 0}, T())
    same('filed', inv['fiscalStatus'], 'Registered')
    rates = {it['ItemName']: it['TaxRate'] for it in mock('GET', '/stats')['last']['body']['Items'] if it['ItemCode'] != 'SERVICE'}
    same('every item filed at 16%', set(rates.values()), {16})
    billing(oneTaxForAllItems=False)


def automatic():
    filing(autoRetry=True)
    mock('POST', '/control', {'mode': 'down'})
    inv = bill_on('T4', line(state['N']))
    same('waits while down', inv['fiscalStatus'], 'Pending')
    mock('POST', '/control', {'mode': 'ok'})
    t0 = time.time()
    while time.time() - t0 < 330 and pending():
        time.sleep(10)
    same('the automatic resend filed it (within 5 minutes)', len(pending()), 0)
    print(f'      automatic resend took {int(time.time() - t0)} s', flush=True)
    filing(autoRetry=False)


def live():
    if not LIVE_IS_MOCK:
        print('      Live filing skipped: TAX_CHECK_LIVE_IS_MOCK is not 1', flush=True)
        return
    same('nothing waiting before going Live', len(pending()), 0)
    branch(expect=400, fiscalAuthority='FBR', fiscalEnvironment='Live', fbrPosId=110014, fbrToken=TOKEN, fbrEndpointOverride=f'{MOCK}/x')
    branch(fiscalAuthority='FBR', fiscalEnvironment='Live', fbrPosId=110014, fbrToken=TOKEN)
    step('no test bills on Live', 'POST', r(f"/admin/fbr/branches/{state['branch']}/test"), token=T(), expect=400)
    inv = bill_on('T5', line(state['K']))
    same('Live bill filed', (inv['fiscalStatus'], inv['fiscalEnvironment']), ('Registered', 'Live'))
    same('to the live address', mock('GET', '/stats')['last']['path'], '/fbr-live/PostData')
    branch(fiscalAuthority='PRA', fiscalEnvironment='Live', fbrPosId=220022, fbrToken=TOKEN)
    inv = bill_on('T6', line(state['C']))
    same('PRA Live bill filed', (inv['fiscalStatus'], inv['fiscalOffice'], inv['fiscalEnvironment']), ('Registered', 'PRA', 'Live'))
    same('to PRA\'s live address', mock('GET', '/stats')['last']['path'], '/pra-live/PostData')
    branch(fiscalEnvironment='Practice', fbrToken='')


def lists():
    filed = step('filed list', 'GET', r('/admin/fbr/invoices/synced'), token=T())
    nums = [f['fiscalNumber'] for f in filed]
    same('every filed bill has its own number', len(nums), len(set(nums)))
    same('nothing left waiting', len(pending()), 0)
    st = mock('GET', '/stats')
    same('the stand-in never saw a bill twice', st['duplicates'], 0)
    same('the stand-in refused nothing it should have taken', [x for x in st['refused'] if 'Invalid PCT code' not in x['why']], [])


for name, fn in (('setup', setup), ('practice', practice), ('settings rules', rules), ('Test: test bill and real bills', test_env),
                 ('office down, resend one / picked / all', outage), ('refused, then resent', refusal),
                 ('never filed twice, send later', never_twice), ('PRA format', pra), ('one tax for all items', one_tax),
                 ('automatic resend', automatic), ('Live (to the stand-in)', live), ('lists', lists)):
    section(name, fn)
print(f'PASSED {passed}, FAILED {len(fails)}')
for f in fails:
    print(' - ' + f)
sys.exit(1 if fails else 0)
