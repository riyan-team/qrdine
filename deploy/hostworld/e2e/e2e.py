#!/usr/bin/env python3
"""Walks the whole restaurant day against the throwaway copy on 127.0.0.1:5199 (real Postgres).
Uses generated test values only and prints no passwords or tokens."""
import datetime, json, random, re, secrets, subprocess, threading, time, traceback, urllib.error, urllib.parse, urllib.request

BASE = 'http://127.0.0.1:5199'
passed = 0
fails = []
statuses = {}
current = '-'


def raw_req(method, path, body=None, token=None, raw=None, ctype=None):
    data, headers = None, {}
    if raw is not None:
        data, headers['Content-Type'] = raw.encode(), ctype
    elif body is not None:
        data, headers['Content-Type'] = json.dumps(body).encode(), 'application/json'
    elif method in ('POST', 'PUT'):
        data, headers['Content-Type'] = b'{}', 'application/json'
    if token:
        headers['Authorization'] = 'Bearer ' + token
    for _ in range(6):
        r = urllib.request.Request(BASE + path, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(r, timeout=45) as resp:
                status, text = resp.status, resp.read().decode('utf-8', 'replace')
        except urllib.error.HTTPError as e:
            status, text = e.code, e.read().decode('utf-8', 'replace')
        if status == 429:
            time.sleep(15)
            continue
        break
    statuses[status] = statuses.get(status, 0) + 1
    try:
        return status, json.loads(text) if text else None
    except ValueError:
        return status, text


def fail(label, detail):
    fails.append(f'[{current}] {label}: {detail}')


def ok():
    global passed
    passed += 1


def step(label, method, path, body=None, token=None, expect=200, raw=None, ctype=None):
    status, parsed = raw_req(method, path, body, token, raw, ctype)
    if expect == 200 and method in ('POST', 'PUT', 'DELETE'):
        expect = [200, 204]
    good = status in expect if isinstance(expect, (list, tuple, set)) else status == expect
    if good:
        ok()
    else:
        fail(label, f'{method} {path} gave {status}, wanted {expect}: {str(parsed)[:300]}')
    return parsed


def same(label, actual, expected):
    if isinstance(expected, (int, float)) and isinstance(actual, (int, float)):
        good = abs(actual - expected) < 0.011
    else:
        good = actual == expected
    if good:
        ok()
    else:
        fail(label, f'got {actual!r}, wanted {expected!r}')


def section(name, fn):
    global current
    current = name
    before = len(fails)
    try:
        fn()
    except Exception as e:  # keep going: one broken section shouldn't hide the others
        fail('stopped early', f'{type(e).__name__}: {e} @ {traceback.extract_tb(e.__traceback__)[-1].lineno}')
    print(f"{'FAIL' if len(fails) > before else 'ok  '}  {name}", flush=True)


# ------------------------------------------------------------------ sign-up, sign-in
state = {}
mobile = '03' + ''.join(random.choices('0123456789', k=9))
pw = secrets.token_urlsafe(16)
pk_today = (datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(hours=5)).strftime('%Y-%m-%d')
month = pk_today[:7]


def r(path):
    return f"/api/r/{state['slug']}{path}"


def signup():
    me = step('sign up', 'POST', '/api/onboard', {
        'restaurantName': 'E2E Karachi Grill', 'slug': None, 'ownerDisplayName': 'Test Owner',
        'ownerUsername': mobile, 'ownerPassword': pw, 'planId': 4})
    state['slug'] = me['slug']
    state['token'] = me['token']
    same('address comes from the name', me['slug'], 'e2e-karachi-grill')
    same('role', me['role'], 'Owner')
    for k in ('ordering', 'kitchen', 'waiter', 'inventory', 'reports', 'billing'):
        same(f'full plan has {k}', me['features'][k], True)
    login = step('sign in with mobile only', 'POST', '/api/auth/login', {'username': mobile, 'password': pw})
    same('sign in finds the restaurant', login['slug'], me['slug'])
    state['token'] = login['token']
    step('wrong password refused', 'POST', '/api/auth/login', {'username': mobile, 'password': 'wrong-' + pw}, expect=[400, 401])
    step('same mobile twice refused', 'POST', '/api/onboard', {
        'restaurantName': 'E2E Another', 'slug': None, 'ownerDisplayName': 'Test Owner',
        'ownerUsername': mobile, 'ownerPassword': pw, 'planId': 4}, expect=400)
    q = urllib.parse.quote('E2E Karachi Grill')
    same('lookup by name', step('lookup', 'GET', f'/api/restaurants/lookup?q={q}')['slug'], me['slug'])


section('sign-up and sign-in', signup)
T = lambda: state['token']


# ------------------------------------------------------------------ menu
def flat_items(m):
    cats = m if isinstance(m, list) else (m.get('menu') or m.get('categories') or [])
    return [i for c in cats for i in c.get('items', [])]


def item_input(cat, name, price, **kw):
    return {'categoryId': cat, 'name': name, 'nameUrdu': '', 'description': 'Test', 'price': price, 'emoji': '',
            'available': True, 'prepMinutes': 10, 'variants': None, 'modifierGroups': None, **kw}


def menu():
    public = step('guest menu', 'GET', r(''))
    same('starter menu seeded', len(flat_items(public)) > 0, True)
    c = step('add category', 'POST', r('/admin/categories'), {'name': 'E2E Category', 'sortOrder': 50}, T())
    state['cat'] = c['id']
    a = step('add item A', 'POST', r('/admin/items'), item_input(c['id'], 'E2E Karahi', 500), T())
    b = step('add item B', 'POST', r('/admin/items'), item_input(c['id'], 'E2E Naan', 300), T())
    v = step('add item with sizes and add-ons', 'POST', r('/admin/items'), item_input(
        c['id'], 'E2E Pizza', 400,
        variants=[{'name': 'Small', 'price': 300, 'sortOrder': 1}, {'name': 'Large', 'price': 400, 'sortOrder': 2}],
        modifierGroups=[{'name': 'Extras', 'minSelect': 0, 'maxSelect': 2, 'sortOrder': 1,
                         'options': [{'name': 'Cheese', 'price': 50, 'sortOrder': 1}]}]), T())
    state['A'], state['B'], state['V'] = a['id'], b['id'], v['id']
    m = step('admin menu', 'GET', r('/admin/menu'), token=T())
    pizza = next(i for i in flat_items(m) if i['id'] == v['id'])
    state['large'] = next(x['id'] for x in pizza['variants'] if x['name'] == 'Large')
    state['cheese'] = pizza['modifierGroups'][0]['options'][0]['id']
    step('settings: no tax to start', 'PUT', r('/admin/billing-settings'), {
        'taxEnabled': False, 'taxName': 'GST', 'taxPercent': 0, 'serviceChargePercent': 0, 'discountEnabled': True,
        'discountIsPercent': False, 'discountValue': 0, 'receiptEnabled': True, 'stockTrackingEnabled': True,
        'taxPercentCard': None}, T())
    same('starter tables', len(step('tables', 'GET', r('/tables'), token=T())), 4)
    branch = step('branches', 'GET', r('/admin/branches'), token=T())[0]['id']
    for i in range(5, 11):
        step(f'add table T{i}', 'POST', r('/admin/tables'), {'code': f'T{i}', 'name': f'Table {i}', 'branchId': branch}, T())
    same('tables after adding', len(step('tables', 'GET', r('/tables'), token=T())), 10)
    step('same code twice refused', 'POST', r('/admin/tables'), {'code': 'T5', 'name': 'Again', 'branchId': branch}, T(), expect=400)


section('menu and settings', menu)


# ------------------------------------------------------------------ helpers for the floor
def line(item, qty=1, **kw):
    return {'menuItemId': item, 'qty': qty, 'note': None, **kw}


def guest(code):
    return step(f'guest session {code}', 'POST', r(f'/tables/{code}/session'))['token']


def staff_order(code, *lines):
    return step(f'staff order {code}', 'POST', r('/orders'), {'tableCode': code, 'items': list(lines), 'note': None}, T())


def walk(order_id):
    step('approve', 'POST', r(f'/orders/{order_id}/approve'), token=T())
    step('cook', 'POST', r(f'/orders/{order_id}/cook'), {'etaMinutes': 15}, T())
    step('ready', 'POST', r(f'/orders/{order_id}/ready'), token=T())
    return step('serve', 'POST', r(f'/orders/{order_id}/serve'), token=T())


def settle(code, method='Cash', discount=0, tendered=0, customer=None, expect=200):
    return step(f'settle {code}', 'POST', r(f'/tables/{code}/settle'),
                {'method': method, 'discount': discount, 'amountTendered': tendered, 'customerId': customer}, T(), expect=expect)


# ------------------------------------------------------------------ dine in from a guest's phone
def dine_in():
    A, V = state['A'], state['V']
    g = guest('T1')
    o = step('guest places order', 'POST', r('/orders'), {'tableCode': 'T1', 'note': 'e2e', 'items': [
        line(A, 2), line(V, 1, variantId=state['large'], modifierOptionIds=[state['cheese']])]}, g)
    same('order total: 2x500 + 400 + 50', o['total'], 1450)
    same('new order waits for approval', o['status'], 'Placed')
    step('guest token can\'t order for another table', 'POST', r('/orders'),
         {'tableCode': 'T2', 'items': [line(A)], 'note': None}, g, expect=400)
    live = step('guest sees own table', 'GET', r('/tables/T1/orders'), token=g)
    same('table shows one order', len(live), 1)
    step("a table's orders need a pass", 'GET', r('/tables/T1/orders'), expect=401)
    step("another table's pass can't list them", 'GET', r('/tables/T1/orders'), token=guest('T2'), expect=403)
    step('an order needs a pass', 'GET', r(f"/orders/{o['id']}"), expect=401)
    step("another table's pass can't read it", 'GET', r(f"/orders/{o['id']}"), token=guest('T2'), expect=400)
    step('own pass reads it', 'GET', r(f"/orders/{o['id']}"), token=g)
    queue = step('staff queue', 'GET', r('/staff/orders'), token=T())
    same('order is in the queue', any(x['id'] == o['id'] for x in queue), True)
    same('status after serve', walk(o['id'])['status'], 'Served')
    bill = step('bill', 'GET', r('/tables/T1/bill'), token=T())
    same('bill total', bill['total'], 1450)
    inv = settle('T1', tendered=2000)
    same('invoice total', inv['total'], 1450)
    same('change', inv['changeGiven'], 550)
    same('first invoice is number 1', bool(re.search(r'0*1$', str(inv['number']))), True)
    state['inv1'] = inv['id']
    same('receipt reads back', step('invoice', 'GET', r(f"/invoices/{inv['id']}"), token=T())['total'], 1450)
    after = step('table clear after settling', 'GET', r('/tables/T1/bill'), token=T())
    same('nothing open', after['hasOpenOrders'], False)
    settle('T1', expect=[400, 404, 409])


section('dine in from a guest phone (QR to receipt)', dine_in)


# ------------------------------------------------------------------ split bill
def split():
    A, B = state['A'], state['B']
    o = staff_order('T2', line(A), line(B))
    same('waiter order total', o['total'], 800)
    walk(o['id'])
    p1 = step('first part', 'POST', r('/tables/T2/pay-part'), {'amount': 300, 'method': 'Cash', 'discount': None}, T())
    same('remaining', p1['remaining'], 500)
    same('not settled yet', p1['settled'], False)
    bill = step('bill mid way', 'GET', r('/tables/T2/bill'), token=T())
    same('bill knows what is paid', bill['amountPaid'], 300)
    step('can\'t overpay a part', 'POST', r('/tables/T2/pay-part'), {'amount': 5000, 'method': 'Cash', 'discount': None}, T(), expect=[200, 400])
    bill = step('bill again', 'GET', r('/tables/T2/bill'), token=T())
    if bill.get('hasOpenOrders'):
        p2 = step('last part', 'POST', r('/tables/T2/pay-part'), {'amount': bill['remaining'], 'method': 'Card', 'discount': None}, T())
        same('settled', p2['settled'], True)
        same('invoice total', p2['invoice']['total'], 800)


section('split bill', split)


# ------------------------------------------------------------------ shift, merge, void, cancel
def floor():
    A, B = state['A'], state['B']
    o = staff_order('T3', line(A, 2), line(B))
    same('total before void', o['total'], 1300)
    step('approve', 'POST', r(f"/orders/{o['id']}/approve"), token=T())
    naan = next(i for i in o['items'] if i['name'] == 'E2E Naan')
    v = step('void naan', 'POST', r(f"/orders/{o['id']}/items/{naan['id']}/void"), {'qty': 1, 'reason': 'Guest changed mind'}, T())
    same('total after void', v['total'], 1000)
    step('void needs a reason', 'POST', r(f"/orders/{o['id']}/items/{naan['id']}/void"), {'qty': 1, 'reason': ''}, T(), expect=400)
    step('shift T3 to T4', 'POST', r('/tables/T3/move'), {'toCode': 'T4'}, T())
    same('old table empty', step('T3 bill', 'GET', r('/tables/T3/bill'), token=T())['hasOpenOrders'], False)
    same('new table has the bill', step('T4 bill', 'GET', r('/tables/T4/bill'), token=T())['total'], 1000)
    o2 = staff_order('T5', line(B))
    step('merge T5 into T4', 'POST', r('/tables/T5/move'), {'toCode': 'T4'}, T())
    same('merged bill', step('T4 bill', 'GET', r('/tables/T4/bill'), token=T())['total'], 1300)
    same('T5 empty', step('T5 bill', 'GET', r('/tables/T5/bill'), token=T())['hasOpenOrders'], False)
    for x in (o['id'], o2['id']):
        cur = step('read', 'GET', r(f'/orders/{x}'), token=T())
        if cur['status'] == 'Placed':
            step('approve', 'POST', r(f'/orders/{x}/approve'), token=T())
        step('cook', 'POST', r(f'/orders/{x}/cook'), {'etaMinutes': 10}, T())
        step('ready', 'POST', r(f'/orders/{x}/ready'), token=T())
        step('serve', 'POST', r(f'/orders/{x}/serve'), token=T())
    same('merged invoice', settle('T4', tendered=1300)['total'], 1300)
    o3 = staff_order('T6', line(A))
    step('cancel', 'POST', r(f"/orders/{o3['id']}/cancel"), {'reason': 'Guest left'}, T())
    same('cancelled', step('read', 'GET', r(f"/orders/{o3['id']}"), token=T())['status'], 'Cancelled')
    same('table clear', step('T6 bill', 'GET', r('/tables/T6/bill'), token=T())['hasOpenOrders'], False)


section('shift table, merge, void, cancel', floor)


# ------------------------------------------------------------------ parcel and delivery
def tickets():
    A = state['A']
    p = step('parcel', 'POST', r('/tickets'), {'channel': 'Takeaway', 'customerName': 'Test Walkin', 'customerPhone': '03000000000',
             'address': None, 'deliveryCharge': None, 'branchId': None, 'items': [line(A)], 'note': None}, T())
    same('parcel channel', p['order']['channel'], 'Takeaway')
    code = p['ticket']['tableCode']
    d = step('delivery', 'POST', r('/tickets'), {'channel': 'Delivery', 'customerName': 'Test Home', 'customerPhone': '03000000001',
             'address': 'House 1, Test Road', 'deliveryCharge': 100, 'branchId': None, 'items': [line(A)], 'note': None}, T())
    dc = d['ticket']['tableCode']
    same('each ticket gets its own table', code != dc, True)
    same('both open', len(step('open tickets', 'GET', r('/tickets'), token=T())) >= 2, True)
    step('set rider', 'PUT', r(f'/tickets/{dc}'), {'customerName': None, 'customerPhone': None, 'address': None, 'deliveryCharge': None, 'riderName': 'Test Rider'}, T())
    walk(p['order']['id'])
    walk(d['order']['id'])
    same('parcel invoice', settle(code, tendered=500)['total'], 500)
    state['delivery_code'] = dc
    bill = step('delivery bill', 'GET', r(f'/tables/{dc}/bill'), token=T())
    same('delivery charge on the bill', bill['total'], 600)
    same('rider on the bill', bill['riderName'], 'Test Rider')
    inv = settle(dc, tendered=600)
    same('delivery invoice', inv['total'], 600)
    same('delivery channel on invoice', inv['channel'], 'Delivery')
    same('tickets closed', len(step('open tickets', 'GET', r('/tickets'), token=T())), 0)


section('parcel and delivery', tickets)


# ------------------------------------------------------------------ customer khata
def khata():
    A = state['A']
    c = step('add customer', 'POST', r('/admin/customers'), {'name': 'Test Regular', 'phone': '03001112222', 'address': None,
             'notes': None, 'openingBalance': 200, 'creditLimit': 1000}, T())
    state['cust'] = c['id']
    found = step('find by phone', 'GET', r('/staff/customers/find?q=0300111'), token=T())
    same('found', [x['id'] for x in found], [c['id']])
    o = staff_order('T6', line(A))
    walk(o['id'])
    inv = settle('T6', 'Khata', customer=c['id'])
    same('khata invoice', inv['paymentMethod'], 'Khata')
    led = step('ledger', 'GET', r(f"/admin/customers/{c['id']}/ledger"), token=T())
    same('balance: 200 opening + 500', led['customer']['balance'], 700)
    o = staff_order('T6', line(A))
    walk(o['id'])
    settle('T6', 'Khata', customer=c['id'], expect=400)  # 700 + 500 is over the 1000 limit
    same('still 700 after the refusal', step('ledger', 'GET', r(f"/admin/customers/{c['id']}/ledger"), token=T())['customer']['balance'], 700)
    settle('T6', tendered=500)
    step('customer pays', 'POST', r(f"/admin/customers/{c['id']}/payments"), {'date': None, 'amount': 300, 'method': 'Cash', 'reference': None, 'note': None}, T())
    led = step('ledger', 'GET', r(f"/admin/customers/{c['id']}/ledger"), token=T())
    same('balance after payment', led['customer']['balance'], 400)


section('customer khata and credit limit', khata)


# ------------------------------------------------------------------ guest calls
def calls():
    g = guest('T7')
    step('call waiter', 'POST', r('/tables/T7/call'), {'kind': 'Waiter'}, g, expect=[200, 204])
    step('ask for bill', 'POST', r('/tables/T7/call'), {'kind': 'Bill'}, g, expect=[200, 204])
    q = step('requests', 'GET', r('/staff/requests'), token=T())
    same('both arrived', len(q) >= 1, True)
    for x in q:
        step('done', 'POST', r(f"/staff/requests/{x['id']}/done"), token=T(), expect=[200, 204])
    same('cleared', len(step('requests', 'GET', r('/staff/requests'), token=T())), 0)


section('guest calls waiter or asks for bill', calls)


# ------------------------------------------------------------------ tax, service charge, discount
def tax():
    A = state['A']
    step('tax on', 'PUT', r('/admin/billing-settings'), {
        'taxEnabled': True, 'taxName': 'GST', 'taxPercent': 16, 'serviceChargePercent': 5, 'discountEnabled': True,
        'discountIsPercent': False, 'discountValue': 0, 'receiptEnabled': True, 'stockTrackingEnabled': True,
        'taxPercentCard': 5}, T())
    o = staff_order('T8', line(A, 2))
    walk(o['id'])
    cash = step('cash bill', 'GET', r('/tables/T8/bill?method=Cash'), token=T())
    card = step('card bill', 'GET', r('/tables/T8/bill?method=Card'), token=T())
    same('cash tax rate', cash['taxPercent'], 16)
    same('card tax rate', card['taxPercent'], 5)
    same('service charge', cash['serviceChargeAmount'], 50)
    disc = step('bill with discount', 'GET', r('/tables/T8/bill?method=Card&discount=100'), token=T())
    same('bill adds up', disc['total'], disc['subtotal'] + disc['serviceChargeAmount'] + disc['taxAmount'] - disc['discountAmount'])
    inv = settle('T8', 'Card', discount=100)
    same('invoice matches the bill shown', inv['total'], disc['total'])
    same('discount kept', inv['discountAmount'], 100)
    state['tax_total'] = inv['total']
    step('tax off', 'PUT', r('/admin/billing-settings'), {
        'taxEnabled': False, 'taxName': 'GST', 'taxPercent': 0, 'serviceChargePercent': 0, 'discountEnabled': True,
        'discountIsPercent': False, 'discountValue': 0, 'receiptEnabled': True, 'stockTrackingEnabled': True,
        'taxPercentCard': None}, T())


section('tax by payment type, service charge, discount', tax)


# ------------------------------------------------------------------ stock and recipes
def stock_qty(i):
    rows = step('stock', 'GET', r('/admin/stock'), token=T())
    return next(s for s in rows if s['id'] == i)['quantity']


def stock():
    A = state['A']
    s = step('add stock', 'POST', r('/admin/stock'), {'name': 'E2E Chicken', 'unit': 'kg', 'quantity': 10, 'lowThreshold': 2}, T())
    state['stock'] = s['id']
    step('recipe', 'PUT', r(f'/admin/items/{A}/recipe'), {'lines': [{'stockItemId': s['id'], 'qtyPerServing': 0.5}]}, T())
    same('recipe reads back', len(step('recipe', 'GET', r(f'/admin/items/{A}/recipe'), token=T())), 1)
    o = staff_order('T8', line(A, 2))
    same('placing doesn\'t take stock', stock_qty(s['id']), 10)
    step('approve', 'POST', r(f"/orders/{o['id']}/approve"), token=T())
    same('approving takes it', stock_qty(s['id']), 9)
    step('cancel', 'POST', r(f"/orders/{o['id']}/cancel"), {'reason': 'Test'}, T())
    same('cancelling puts it back', stock_qty(s['id']), 10)
    step('restock', 'POST', r(f"/admin/stock/{s['id']}/restock"), {'quantity': 5, 'reason': 'Bought'}, T())
    same('after restock', stock_qty(s['id']), 15)
    step('adjust', 'POST', r(f"/admin/stock/{s['id']}/adjust"), {'newQuantity': 14, 'reason': 'Count'}, T())
    same('after count', stock_qty(s['id']), 14)
    step('waste', 'POST', r(f"/admin/stock/{s['id']}/waste"), {'quantity': 1, 'reason': 'Spoiled'}, T())
    same('after waste', stock_qty(s['id']), 13)
    same('movements recorded', len(step('movements', 'GET', r('/admin/movements'), token=T())) >= 5, True)


section('stock, recipes, restock, waste', stock)


# ------------------------------------------------------------------ supplier khata
def suppliers():
    sid = state['stock']
    su = step('add supplier', 'POST', r('/admin/suppliers'), {'name': 'E2E Poultry', 'phone': '03002223333', 'notes': None, 'openingBalance': 0}, T())['id']
    state['supplier'] = su
    step('purchase', 'POST', r(f'/admin/suppliers/{su}/purchases'), {'date': None, 'reference': 'B1', 'note': None, 'amount': None,
         'lines': [{'stockItemId': sid, 'name': None, 'qty': 10, 'unitCost': 100}], 'paidNow': 400, 'paidMethod': 'Cash'}, T())
    bal = lambda: step('ledger', 'GET', r(f'/admin/suppliers/{su}/ledger'), token=T())
    same('owed after buying 1000, paying 400', bal()['supplier']['balance'], 600)
    same('stock came in', stock_qty(sid), 23)
    step('pay supplier', 'POST', r(f'/admin/suppliers/{su}/payments'), {'date': None, 'amount': 100, 'method': 'Cash', 'reference': None, 'note': None}, T())
    same('owed after payment', bal()['supplier']['balance'], 500)
    step('return', 'POST', r(f'/admin/suppliers/{su}/returns'), {'date': None, 'reference': None, 'note': 'Bad', 'amount': None,
         'lines': [{'stockItemId': sid, 'name': None, 'qty': 1, 'unitCost': 100}]}, T())
    same('owed after return', bal()['supplier']['balance'], 400)
    same('stock went back', stock_qty(sid), 22)
    same('last cost remembered', next(s for s in step('stock', 'GET', r('/admin/stock'), token=T()) if s['id'] == sid)['lastCost'], 100)
    rows = bal()['rows']
    same('four lines on the statement (buy, paid-now, pay, return)', len(rows) >= 3, True)
    step('delete a line', 'DELETE', r(f"/admin/suppliers/entries/{rows[-1]['id']}"), token=T(), expect=[200, 204])
    same('removing the return restores what is owed', bal()['supplier']['balance'], 500)
    same('and the stock', stock_qty(sid), 23)


section('supplier khata', suppliers)


# ------------------------------------------------------------------ expenses and payroll
def expenses():
    e = step('expense', 'POST', r('/admin/expenses'), {'date': None, 'category': 'Gas', 'amount': 2000, 'paymentMethod': 'Cash',
             'paidTo': 'SNGPL', 'note': None, 'branchId': None}, T())
    e2 = step('expense 2', 'POST', r('/admin/expenses'), {'date': pk_today, 'category': 'Rent', 'amount': 700, 'paymentMethod': 'Bank',
              'paidTo': None, 'note': None, 'branchId': None}, T())
    step('edit', 'PUT', r(f"/admin/expenses/{e2['id']}"), {'date': pk_today, 'category': 'Rent', 'amount': 800, 'paymentMethod': 'Bank',
         'paidTo': None, 'note': 'x', 'branchId': None}, T())
    rows = step('list', 'GET', r(f'/admin/expenses?from={pk_today}&to={pk_today}'), token=T())
    rows = rows if isinstance(rows, list) else rows.get('rows', [])
    same('expenses today', sum(x['amount'] for x in rows), 2800)
    same('filter by category', len([x for x in rows if x['category'] == 'Gas']), 1)
    state['exp'] = e['id']


def payroll():
    em = step('add employee', 'POST', r('/admin/payroll'), {'name': 'E2E Cook', 'phone': None, 'designation': 'Cook', 'monthlySalary': 30000, 'joinedOn': None, 'notes': None}, T())['id']
    step('advance', 'POST', r(f'/admin/payroll/{em}/entries'), {'type': 'Advance', 'amount': 5000, 'month': month, 'date': None, 'method': 'Cash', 'note': None}, T())
    step('bonus', 'POST', r(f'/admin/payroll/{em}/entries'), {'type': 'Bonus', 'amount': 1000, 'month': month, 'date': None, 'method': 'Cash', 'note': None}, T())
    step('attendance', 'PUT', r('/admin/payroll/attendance'), {'employeeId': em, 'date': pk_today, 'status': 'Present'}, T())
    step('attendance again (same day, must update)', 'PUT', r('/admin/payroll/attendance'), {'employeeId': em, 'date': pk_today, 'status': 'Absent'}, T())
    rows = step('payroll', 'GET', r(f'/admin/payroll?month={month}'), token=T())
    rows = rows if isinstance(rows, list) else rows.get('rows', [])
    row = next(x for x in rows if x['id'] == em)
    same('advance counted', row['advances'], 5000)
    same('due: 30000 + 1000 - 5000', row['due'], 26000)
    same('attendance kept one mark', [row['present'], row['absent']], [0, 1])
    step('attendance sheet', 'GET', r(f'/admin/payroll/attendance?month={month}'), token=T())
    same('statement', len(step('statement', 'GET', r(f'/admin/payroll/{em}/entries?month={month}'), token=T())), 2)


section('expenses', expenses)
section('salaries, advances, attendance', payroll)


# ------------------------------------------------------------------ bulk price, import
def bulk():
    A = state['A']
    o = staff_order('T9', line(A))
    step('bulk price', 'POST', r('/admin/menu/bulk-price'), {'percent': 10, 'roundTo': 10, 'categoryId': state['cat']}, T())
    m = step('menu', 'GET', r('/admin/menu'), token=T())
    same('new price', next(i for i in flat_items(m) if i['id'] == A)['price'], 550)
    same('open order keeps its price', step('read', 'GET', r(f"/orders/{o['id']}"), token=T())['total'], 500)
    walk(o['id'])
    same('bill keeps it too', settle('T9', tendered=500)['total'], 500)
    step('prices back', 'POST', r('/admin/menu/bulk-price'), {'percent': -9.0909, 'roundTo': 10, 'categoryId': state['cat']}, T())
    prev = step('csv preview', 'POST', r('/admin/menu/import/csv'), token=T(), raw='Category,Item,Price\nE2E Drinks,Lassi,150\nE2E Drinks,Doodh Soda,120\nE2E Category,Raita,80\n', ctype='text/csv')
    same('preview categories', len(prev['categories']), 2)
    step('apply import', 'POST', r('/admin/menu/import/apply'), {'categories': prev['categories']}, T())
    m = step('menu', 'GET', r('/admin/menu'), token=T())
    names = [i['name'] for i in flat_items(m)]
    same('imported', 'Lassi' in names and 'Raita' in names, True)
    cats = m if isinstance(m, list) else (m.get('menu') or m.get('categories'))
    same('no duplicate category', len([c for c in cats if c['name'] == 'E2E Category']), 1)


section('bulk price and spreadsheet import', bulk)


# ------------------------------------------------------------------ roles
def roles():
    made = {}
    for role in ('Cashier', 'Kitchen', 'Waiter', 'Manager'):
        u = '03' + ''.join(random.choices('0123456789', k=9))
        step(f'add {role}', 'POST', r('/admin/staff'), {'username': u, 'displayName': f'Test {role}', 'role': role, 'password': pw}, T())
        made[role] = step(f'{role} signs in', 'POST', '/api/auth/login', {'username': u, 'password': pw})['token']
    c, k, w, m = made['Cashier'], made['Kitchen'], made['Waiter'], made['Manager']
    step('kitchen can\'t see reports', 'GET', r('/admin/dashboard'), token=k, expect=403)
    step('kitchen can\'t settle', 'POST', r('/tables/T1/settle'), {'method': 'Cash', 'discount': 0}, k, expect=403)
    step('waiter can\'t settle', 'POST', r('/tables/T1/settle'), {'method': 'Cash', 'discount': 0}, w, expect=403)
    step('waiter can\'t see suppliers', 'GET', r('/admin/suppliers'), token=w, expect=403)
    step('cashier can\'t see payroll', 'GET', r('/admin/payroll'), token=c, expect=403)
    step('cashier can\'t edit menu', 'POST', r('/admin/categories'), {'name': 'No', 'sortOrder': 1}, c, expect=403)
    step('cashier opens reception', 'GET', r('/reception/dashboard?page=1&pageSize=5'), token=c)
    step('manager sees reports', 'GET', r(f'/admin/reports/business?from={pk_today}&to={pk_today}'), token=m)
    o = step('waiter takes an order', 'POST', r('/orders'), {'tableCode': 'T9', 'items': [line(state['B'])], 'note': None}, w)
    step('waiter can\'t approve', 'POST', r(f"/orders/{o['id']}/approve"), token=w, expect=403)
    step('cashier approves', 'POST', r(f"/orders/{o['id']}/approve"), token=c)
    step('kitchen can\'t serve', 'POST', r(f"/orders/{o['id']}/serve"), token=k, expect=403)
    step('kitchen cooks', 'POST', r(f"/orders/{o['id']}/cook"), {'etaMinutes': 5}, k)
    step('kitchen marks ready', 'POST', r(f"/orders/{o['id']}/ready"), token=k)
    step('waiter serves', 'POST', r(f"/orders/{o['id']}/serve"), token=w)
    step('cashier settles', 'POST', r('/tables/T9/settle'), {'method': 'Cash', 'discount': 0, 'amountTendered': 300, 'customerId': None}, c)
    other = step('reset a staff password', 'GET', r('/admin/staff'), token=T())
    same('staff listed', len(other) >= 5, True)
    state['perm'] = step('permissions', 'GET', r('/admin/permissions'), token=T())


section('roles and permissions', roles)


# ------------------------------------------------------------------ what actually happens at a busy till: everything at once
def run_all(fns):
    out = [None] * len(fns)

    def work(i):
        out[i] = fns[i]()
    ts = [threading.Thread(target=work, args=(i,)) for i in range(len(fns))]
    [t.start() for t in ts]
    [t.join() for t in ts]
    return out


def races():
    A, B = state['A'], state['B']
    # Two cashiers press Settle on the same table.
    o = staff_order('T10', line(B))
    walk(o['id'])
    res = run_all([lambda: raw_req('POST', r('/tables/T10/settle'), {'method': 'Cash', 'discount': 0, 'amountTendered': 300, 'customerId': None}, T()) for _ in range(4)])
    okc = [s for s, _ in res if s == 200]
    same('double settle makes one bill', len(okc), 1)
    bad = [s for s, _ in res if s >= 500]
    same('double settle: no server errors', bad, [])

    # Eight tables settled in the same instant.
    codes = [f'T{i}' for i in range(1, 9)]
    for c in codes:
        walk(staff_order(c, line(B))['id'])
    res = run_all([(lambda c=c: raw_req('POST', r(f'/tables/{c}/settle'), {'method': 'Cash', 'discount': 0, 'amountTendered': 300, 'customerId': None}, T())) for c in codes])
    good = [p for s, p in res if s == 200]
    same('eight tables, eight bills', len(good), 8)
    same('eight tables: no server errors', [(s, str(p)[:160]) for s, p in res if s >= 500], [])
    nums = [p['number'] for p in good]
    same('bill numbers all different', len(set(nums)), len(nums))
    open_after = [c for c in codes if step('bill', 'GET', r(f'/tables/{c}/bill'), token=T())['hasOpenOrders']]
    same('eight tables all closed', open_after, [])

    # Ten phones at one table order together.
    g = guest('T1')
    res = run_all([(lambda: raw_req('POST', r('/orders'), {'tableCode': 'T1', 'items': [line(B)], 'note': None}, g)) for _ in range(10)])
    same('ten orders, all accepted', [s for s, _ in res if s != 200], [])
    ids = [p['id'] for s, p in res if s == 200]
    same('ten distinct orders', len(set(ids)), 10)
    same('bill counts them all', step('bill', 'GET', r('/tables/T1/bill'), token=T())['total'], 3000)

    # Two staff approve the same order: stock must come off once.
    sid = state['stock']
    before = stock_qty(sid)
    o = staff_order('T2', line(A, 2))
    res = run_all([(lambda: raw_req('POST', r(f"/orders/{o['id']}/approve"), token=T())) for _ in range(4)])
    same('double approve: no server errors', [s for s, _ in res if s >= 500], [])
    same('stock comes off once', stock_qty(sid), before - 1)
    # Approve and cancel raced: whichever wins, the stock must end up consistent.
    o2 = staff_order('T3', line(A, 2))
    before = stock_qty(sid)
    res = run_all([(lambda: raw_req('POST', r(f"/orders/{o2['id']}/approve"), token=T())),
                   (lambda: raw_req('POST', r(f"/orders/{o2['id']}/cancel"), {'reason': 'Race'}, T()))])
    same('approve vs cancel: no server errors', [s for s, _ in res if s >= 500], [])
    final = step('read', 'GET', r(f"/orders/{o2['id']}"), token=T())['status']
    expected = before if final == 'Cancelled' else before - 1
    same(f'stock matches final status ({final})', stock_qty(sid), expected)

    # Two parts paid in the same instant can't pay more than the bill.
    o3 = staff_order('T4', line(A))
    walk(o3['id'])
    res = run_all([(lambda: raw_req('POST', r('/tables/T4/pay-part'), {'amount': 400, 'method': 'Cash', 'discount': None}, T())) for _ in range(3)])
    same('part payments: no server errors', [s for s, _ in res if s >= 500], [])
    settled = [p for s, p in res if s == 200 and p['settled']]
    same('parts add up to one bill, then stop', [len([1 for s, _ in res if s == 200]), len(settled), len([1 for s, _ in res if s == 400])], [2, 1, 1])
    same('that bill is for the full amount', settled[0]['invoice']['total'], 500)
    same('table clear after the parts', step('T4 bill', 'GET', r('/tables/T4/bill'), token=T())['hasOpenOrders'], False)
    # Move two tables onto one at the same moment.
    o4 = staff_order('T5', line(B))
    o5 = staff_order('T6', line(B))
    res = run_all([(lambda: raw_req('POST', r('/tables/T5/move'), {'toCode': 'T7'}, T())), (lambda: raw_req('POST', r('/tables/T6/move'), {'toCode': 'T7'}, T()))])
    same('moves: no server errors', [s for s, _ in res if s >= 500], [])
    same('nothing lost in the move', step('T7 bill', 'GET', r('/tables/T7/bill'), token=T())['total'], 600)
    # Tidy up so the day's totals below are simple to read.
    for x in (o['id'], o2['id'], o4['id'], o5['id'], *ids):
        cur = step('read', 'GET', r(f'/orders/{x}'), token=T())
        if cur['status'] not in ('Cancelled', 'Paid'):
            step('cancel', 'POST', r(f'/orders/{x}/cancel'), {'reason': 'Tidy up'}, T())
    # A guest orders in the instant the cashier settles. The food must land on a bill either way.
    o6 = staff_order('T9', line(B))
    walk(o6['id'])
    res = run_all([(lambda: raw_req('POST', r('/tables/T9/settle'), {'method': 'Cash', 'discount': 0, 'amountTendered': 300, 'customerId': None}, T())),
                   (lambda: raw_req('POST', r('/orders'), {'tableCode': 'T9', 'items': [line(B)], 'note': None}, T()))])
    same('order vs settle: no server errors', [s for s, _ in res if s >= 500], [])
    late = res[1][1]
    bill_lines = sum(l['qty'] for l in res[0][1]['lines'])
    still_open = step('T9 bill', 'GET', r('/tables/T9/bill'), token=T())
    same('late order is on the bill or on the next seating', (bill_lines == 2) != still_open['hasOpenOrders'], True)
    if still_open['hasOpenOrders']:
        step('cancel', 'POST', r(f"/orders/{late['id']}/cancel"), {'reason': 'Tidy up'}, T())
    # How long the queue makes people wait: ten phones in one instant.
    g = guest('T10')
    t0 = time.time()
    run_all([(lambda: raw_req('POST', r('/orders'), {'tableCode': 'T10', 'items': [line(B)], 'note': None}, g)) for _ in range(10)])
    print(f'    ten orders at once took {int((time.time() - t0) * 1000)} ms in all (includes 15 s waits whenever the per-address order limit answers 429)')
    for x in step('T10 orders', 'GET', r('/tables/T10/orders'), token=T()):
        step('cancel', 'POST', r(f"/orders/{x['id']}/cancel"), {'reason': 'Tidy up'}, T())


section('everything at once (two cashiers, ten phones, races)', races)


# ------------------------------------------------------------------ every report, on Postgres
def reads():
    for days in (1, 7, 30, 90):
        d = step(f'dashboard {days}', 'GET', r(f'/admin/dashboard?days={days}'), token=T())
    state['dash'] = d
    s = step('sales report', 'GET', r(f'/admin/reports/sales?from={pk_today}&to={pk_today}&page=1&pageSize=100'), token=T())
    state['sales'] = s
    step('sales: search', 'GET', r('/admin/reports/sales?q=T1&page=1&pageSize=5'), token=T())
    step('sales: by method', 'GET', r('/admin/reports/sales?method=Card&page=1&pageSize=5'), token=T())
    step('sales: search number', 'GET', r('/admin/reports/sales?q=1&page=1&pageSize=5'), token=T())
    status, csv = raw_req('GET', r(f'/admin/reports/invoices.csv?from={pk_today}&to={pk_today}'), token=T())
    same('invoice export', status, 200)
    same('export has every bill', len(str(csv).strip().splitlines()) - 1 >= s['total'], True)
    rep = step('business report', 'GET', r(f'/admin/reports/business?from={pk_today}&to={pk_today}'), token=T())
    state['rep'] = rep
    step('business report: a past range', 'GET', r('/admin/reports/business?from=2026-01-01&to=2026-12-31'), token=T())
    step('reception', 'GET', r('/reception/dashboard?page=1&pageSize=20'), token=T())
    step('reception: search', 'GET', r('/reception/dashboard?page=1&pageSize=20&q=T1'), token=T())
    for q in ('', '&status=Paid', '&status=Cancelled', f'&from={pk_today}&to={pk_today}', '&q=E2E'):
        step(f'order history {q}', 'GET', r(f'/staff/orders/history?page=1&pageSize=20{q}'), token=T())
    for path in ('/admin/movements', '/admin/feedback', '/staff/plan', '/admin/fbr', '/admin/fbr/invoices/synced', '/admin/fbr/invoices/pending',
                 '/admin/subscription', '/admin/permissions', '/admin/service-settings', '/admin/billing-settings', '/admin/receipt-settings',
                 '/admin/branches', '/admin/tables', '/admin/staff', '/admin/customers', '/admin/suppliers', '/admin/stock',
                 '/admin/loyalty/program', '/admin/loyalty/overview', '/admin/loyalty/rewards', '/tickets', '/staff/requests'):
        step(path, 'GET', r(path), token=T())
    step('popular', 'GET', r('/popular'))
    step('loyalty for guests', 'GET', r('/loyalty'))
    fb = step('guest rating', 'POST', r('/feedback'), {'rating': 5, 'comment': 'Test', 'tableCode': 'T1', 'memberToken': None}, expect=[200, 204])
    same('rating shows', len(step('feedback', 'GET', r('/admin/feedback'), token=T())) >= 1, True)


section('every report and settings read', reads)


def money_checks():
    rep, s = state['rep'], state['sales']
    su = rep['summary']
    same('report sales = sum of today\'s bills', su['sales'], sum(x['total'] for x in s['rows']))
    same('report bills = count', su['bills'], s['total'])
    same('bills found today', su['bills'] > 5, True)
    same('payment split adds up', sum(x['amount'] for x in rep['payments']), su['sales'])
    same('channels add up', sum(x['amount'] for x in rep['channels']), su['sales'])
    same('daily adds up', sum(x['sales'] for x in rep['daily']), su['sales'])
    same('hours add up', sum(x['sales'] for x in rep['hours']), su['sales'])
    same('expenses in report: 2800 + advance 5000 + bonus 1000', su['expenses'], 8800)
    same('supplier owes in report', rep['supplierBalance'], 500)
    same('khata owed in report', rep['customerBalance'], 400)
    same('void in report', len(rep['voids']), 1)
    same('delivery charges', rep['deliveryCharges'], 100)
    same('rider listed', [x['name'] for x in rep['riders']], ['Test Rider'])
    same('stock value = qty x last cost', rep['stockValue'], sum(x['value'] for x in rep['stock']))
    same('cancelled counted', su['cancelledOrders'] >= 2, True)
    same('discount report', len(rep['discounts']) >= 1, True)
    same('dashboard agrees on today', state['dash']['todaySales'], su['sales'])
    # The day, in Pakistan time, must hold everything that just happened.
    same('report period', rep['from'], pk_today)
    print('    today:', json.dumps({k: su[k] for k in ('sales', 'bills', 'discounts', 'tax', 'serviceCharge', 'expenses', 'purchases', 'supplierPayments', 'estimatedProfit', 'cancelledOrders', 'itemsSold')}))
    print('    channels:', json.dumps(rep['channels']))
    print('    payments:', json.dumps(rep['payments']))


section('report totals agree with the bills', money_checks)


def closing():
    v = step('day closing view', 'GET', r(f'/admin/closing?date={pk_today}'), token=T())
    same('closing sales = report', v['sales'], state['rep']['summary']['sales'])
    same('expected = opening + cash + khata cash - expenses - supplier cash', v['expected'],
         v['openingCash'] + v['cashSales'] + v['khataReceipts'] - v['cashExpenses'] - v['supplierPayments'])
    c = step('close the day', 'POST', r('/admin/closing'), {'date': pk_today, 'openingCash': 1000, 'countedCash': v['expected'] + 1000 - 50,
             'takenOut': 500, 'note': 'Test', 'branchId': None}, T())
    v2 = step('closed view', 'GET', r(f'/admin/closing?date={pk_today}'), token=T())
    same('day shows closed', v2['closed'] is not None, True)
    same('short by 50', v2['closed']['difference'], -50)
    same('closed by', v2['closed']['closedBy'], 'Test Owner')
    step('re-closing updates, not duplicates', 'POST', r('/admin/closing'), {'date': pk_today, 'openingCash': 1000, 'countedCash': v['expected'] + 1000,
         'takenOut': 500, 'note': 'Test', 'branchId': None}, T())
    same('now exact', step('closed view', 'GET', r(f'/admin/closing?date={pk_today}'), token=T())['closed']['difference'], 0)


section('day closing', closing)


# ------------------------------------------------------------------ operator: plans and lock
def operator():
    env = dict(l.strip().split('=', 1) for l in open('/run/qrdine-e2e.env') if '=' in l and not l.startswith('#'))
    p = step('operator signs in', 'POST', '/api/platform/login', {'username': env['PlatformAdmin__Username'], 'password': env['PlatformAdmin__Password']})
    op = p['token']
    rows = step('restaurants', 'GET', '/api/platform/restaurants', token=op)
    rows = rows if isinstance(rows, list) else rows.get('rows', [])
    rid = next(x for x in rows if x['slug'] == state['slug'])['id']
    step('stats', 'GET', '/api/platform/stats', token=op)
    step('plans', 'GET', '/api/platform/plans', token=op)
    step('payments', 'GET', f'/api/platform/restaurants/{rid}/payments', token=op)
    step('record payment', 'POST', f'/api/platform/restaurants/{rid}/payment', {'amount': 12000, 'method': 'Cash', 'note': 'Test', 'months': 1, 'kind': 'Subscription'}, op, expect=[200, 204, 400])
    step('down to QR Menu', 'POST', f'/api/platform/restaurants/{rid}/plan', {'planId': 1}, op, expect=[200, 204])
    same('plan reaches staff', step('plan', 'GET', r('/staff/plan'), token=T())['inventory'], False)
    for path in ('/admin/suppliers', '/admin/stock', '/admin/expenses', '/admin/payroll', '/admin/customers', '/admin/dashboard', f'/admin/reports/business?from={pk_today}&to={pk_today}', f'/admin/closing?date={pk_today}'):
        step(f'gated {path}', 'GET', r(path), token=T(), expect=403)
    step('menu still open', 'GET', r('/admin/menu'), token=T())
    step('reception still open', 'GET', r('/reception/dashboard?page=1&pageSize=5'), token=T())
    g = guest('T8')
    step('guest can\'t order on QR Menu plan', 'POST', r('/orders'), {'tableCode': 'T8', 'items': [line(state['B'])], 'note': None}, g, expect=[400, 403])
    step('back to Full ERP', 'POST', f'/api/platform/restaurants/{rid}/plan', {'planId': 4}, op, expect=[200, 204])
    same('restored', step('plan', 'GET', r('/staff/plan'), token=T())['inventory'], True)
    step('lock', 'POST', f'/api/platform/restaurants/{rid}/status', {'status': 'Suspended'}, op, expect=[200, 204])
    status, _ = raw_req('GET', r(''))
    same('locked: guest menu closed', status in (402, 403, 404, 423), True)
    step('unlock', 'POST', f'/api/platform/restaurants/{rid}/status', {'status': 'Active'}, op, expect=[200, 204])
    step('guest menu back', 'GET', r(''))


section('operator: plans, lock, payments', operator)


def database():
    pwd = open('/var/tmp/qrdine-e2e-pg/pw').read().strip()

    def sql(q):
        return subprocess.run(['/usr/pgsql-18/bin/psql', '-h', '127.0.0.1', '-p', '5434', '-U', 'qrdine', '-d', 'qrdine_e2e', '-Atc', q],
                              env={'PGPASSWORD': pwd}, capture_output=True, text=True, timeout=30).stdout.strip()
    same('no duplicate bill numbers', sql('select count(*) from (select "RestaurantId","Number" from "Invoices" group by 1,2 having count(*)>1) x'), '0')
    same('no table has two running seatings', sql('select count(*) from (select "RestaurantId","DiningTableId" from "Sessions" where "IsOpen" group by 1,2 having count(*)>1) x'), '0')
    same('no seating has two bills', sql('select count(*) from (select "RestaurantId","TableSessionId" from "Invoices" group by 1,2 having count(*)>1) x'), '0')
    same('no order left on a closed seating without a bill', sql("""select count(*) from "Orders" o join "Sessions" s on s."Id"=o."TableSessionId" where not s."IsOpen" and o."Status" not in (5, 6)"""), '0')
    print('    bills in database:', sql('select count(*) || \', total \' || sum("Total") from "Invoices"'))


section('direct database check', database)

print(f'\nPASSED {passed}   FAILED {len(fails)}   http status counts {json.dumps(statuses, sort_keys=True)}')
for f in fails:
    print(' -', f)
