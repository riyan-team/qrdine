#!/usr/bin/env python3
"""A stand-in for FBR's and PRA's invoice servers, for testing only. It never talks to the real offices.

POST /fbr/...  answers like FBR (DateTime "yyyy-MM-dd HH:mm:ss", BuyerNTN).
POST /pra/...  answers like PRA (DateTime with milliseconds, BuyerPNTN, never BuyerNTN).
It checks what the real servers check before giving a number:
  - the bearer token (401 with fault 900908, like the gateway, when it is wrong or missing);
  - the fields: POSID, USIN, DateTime, PaymentMode, InvoiceType, at least one item, each with a PCT code;
  - the sums: each item's tax is (sale value - discount) x rate, each item's total adds up, and the bill's totals
    are the sums of its items;
  - a USIN already filed for that POSID is refused (Code 0, "already exists"), so a bill sent twice shows up.
Control (for the tests): POST /control {"mode": "ok" | "down" | "refuse"}, GET /stats, POST /reset.
Run alone:  python mock_tax_office.py 8599 test-token
"""
import json, re, sys, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

STATE = {'mode': 'ok', 'token': 'test-token', 'filed': {}, 'refused': [], 'requests': 0, 'duplicates': 0, 'last': None}
LOCK = threading.Lock()
FBR_DATE = re.compile(r'^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$')
PRA_DATE = re.compile(r'^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}$')


def near(a, b):
    return abs(float(a) - float(b)) <= 0.0101


def problems(office, b):
    out = []
    for k in ('POSID', 'USIN', 'DateTime', 'Items', 'TotalBillAmount', 'TotalSaleValue', 'TotalTaxCharged', 'TotalQuantity',
              'Discount', 'FurtherTax', 'PaymentMode', 'InvoiceType'):
        if k not in b:
            out.append(f'{k} is missing')
    if out:
        return out
    if not isinstance(b['POSID'], int) or b['POSID'] <= 0:
        out.append('POSID must be a number above 0')
    if not str(b['USIN']).strip():
        out.append('USIN is empty')
    if not (PRA_DATE if office == 'PRA' else FBR_DATE).match(str(b['DateTime'])):
        out.append(f"DateTime {b['DateTime']!r} is not in {office}'s format")
    if office == 'PRA' and 'BuyerNTN' in b:
        out.append('PRA takes BuyerPNTN, not BuyerNTN')
    if office == 'FBR' and 'BuyerPNTN' in b:
        out.append('FBR takes BuyerNTN, not BuyerPNTN')
    if b['PaymentMode'] not in (1, 2, 3, 4, 5, 6):
        out.append(f"PaymentMode {b['PaymentMode']} is not 1-6")
    items = b['Items'] or []
    if not items:
        out.append('no items')
    for i, it in enumerate(items):
        name = it.get('ItemName') or f'item {i + 1}'
        if not it.get('PCTCode') and it.get('ItemCode') != 'SERVICE':
            out.append(f'{name}: PCTCode is empty')
        sale, disc, rate = it['SaleValue'], it['Discount'], it['TaxRate']
        if not near(round((sale - disc) * rate / 100, 2), it['TaxCharged']):
            out.append(f"{name}: tax {it['TaxCharged']} is not {rate}% of {sale - disc:.2f}")
        if not near(sale - disc + it['TaxCharged'] + it['FurtherTax'], it['TotalAmount']):
            out.append(f"{name}: total {it['TotalAmount']} doesn't add up")
    if items:
        for total, field in (('TotalSaleValue', 'SaleValue'), ('TotalTaxCharged', 'TaxCharged'), ('TotalQuantity', 'Quantity'),
                             ('Discount', 'Discount'), ('FurtherTax', 'FurtherTax'), ('TotalBillAmount', 'TotalAmount')):
            if not near(sum(it[field] for it in items), b[total]):
                out.append(f'{total} {b[total]} is not the sum of the items ({sum(it[field] for it in items):.2f})')
    return out


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def answer(self, code, body):
        data = json.dumps(body).encode()
        self.send_response(code)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.path.startswith('/stats'):
            with LOCK:
                return self.answer(200, {'mode': STATE['mode'], 'requests': STATE['requests'], 'filed': len(STATE['filed']),
                                         'duplicates': STATE['duplicates'], 'refused': STATE['refused'][-20:],
                                         'usins': sorted(STATE['filed'].keys()), 'last': STATE['last']})
        self.answer(404, {'error': 'not found'})

    def do_POST(self):
        size = int(self.headers.get('Content-Length') or 0)
        raw = self.rfile.read(size) if size else b''
        if self.path.startswith('/control'):
            body = json.loads(raw or b'{}')
            with LOCK:
                STATE['mode'] = body.get('mode', STATE['mode'])
                if body.get('token'):
                    STATE['token'] = body['token']
            return self.answer(200, {'mode': STATE['mode']})
        if self.path.startswith('/reset'):
            with LOCK:
                STATE.update(filed={}, refused=[], requests=0, duplicates=0, last=None, mode='ok')
            return self.answer(200, {'ok': True})

        office = 'PRA' if self.path.lower().startswith('/pra') else 'FBR'
        with LOCK:
            STATE['requests'] += 1
            mode = STATE['mode']
        if mode == 'down':
            return self.answer(502, {'error': 'Bad Gateway'})
        if self.headers.get('Authorization') != 'Bearer ' + STATE['token']:
            return self.answer(401, {'fault': {'code': 900908, 'message': 'Resource forbidden', 'description': 'Invalid token or IP'}})
        try:
            b = json.loads(raw)
        except ValueError:
            return self.answer(400, {'Code': '400', 'Response': 'Bad request', 'Errors': 'Body is not JSON'})
        with LOCK:
            STATE['last'] = {'office': office, 'path': self.path, 'body': b}
        why = problems(office, b)
        if mode == 'refuse':
            why = ['Invalid PCT code'] + why
        if why:
            with LOCK:
                STATE['refused'].append({'usin': b.get('USIN'), 'why': why})
            return self.answer(200, {'InvoiceNumber': 'Not Available', 'Code': '401', 'Response': 'Invoice not received', 'Errors': '; '.join(why)})
        key = f"{b['POSID']}:{b['USIN']}"
        with LOCK:
            if key in STATE['filed']:
                STATE['duplicates'] += 1
                return self.answer(200, {'InvoiceNumber': 'Not Available', 'Code': '0', 'Response': 'Invoice already exists', 'Errors': f"USIN {b['USIN']} already filed"})
            number = f"{'PRA' if office == 'PRA' else ''}{b['POSID']}{time.strftime('%d%m%y%H%M%S')}{len(STATE['filed']) + 1:04d}"
            STATE['filed'][key] = number
        time.sleep(0.15)   # a real office takes a moment
        self.answer(200, {'InvoiceNumber': number, 'Code': '100', 'Response': 'Fiscal Invoice Number generated successfully.', 'Errors': None})


def start(port, token):
    STATE['token'] = token
    srv = ThreadingHTTPServer(('127.0.0.1', port), Handler)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    return srv


if __name__ == '__main__':
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8599
    start(port, sys.argv[2] if len(sys.argv) > 2 else 'test-token')
    print(f'stand-in tax office on http://127.0.0.1:{port} (fbr: /fbr/PostData, pra: /pra/PostData)', flush=True)
    while True:
        time.sleep(3600)
