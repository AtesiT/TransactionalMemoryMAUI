"""
Симуляция STM и CAS на Python — для проверки логики без .NET SDK в этой среде.
Демонстрирует оба примера из задания.
"""

import threading
import random
import time
from concurrent.futures import ThreadPoolExecutor

# === Пример 1: CAS через Compare-And-Swap ===

class CasAccount:
    def __init__(self):
        self._balance = 0
        self._lock = threading.Lock()  # для атомарности CAS в Python

    @property
    def balance(self):
        return self._balance

    def deposit(self, amount, on_conflict=None):
        attempts = 0
        while True:
            attempts += 1
            current = self._balance
            updated = current + amount
            # имитация работы
            time.sleep(random.uniform(0.0001, 0.0005))
            # CAS
            with self._lock:
                if self._balance == current:
                    self._balance = updated
                    return attempts
            if on_conflict:
                on_conflict()

def demo1():
    print("=== ДЕМО 1: CAS (Interlocked.CompareExchange) ===")
    account = CasAccount()
    conflicts = 0
    lock = threading.Lock()

    def worker(wid):
        nonlocal conflicts
        def on_conflict():
            nonlocal conflicts
            with lock:
                conflicts += 1
        attempts = account.deposit(10, on_conflict)
        print(f"Поток {wid:2}: +10, попыток {attempts}")

    with ThreadPoolExecutor(max_workers=100) as ex:
        ex.map(worker, range(100))

    print(f"Итог: баланс={account.balance} (ожидалось 1000) {'✓ OK' if account.balance==1000 else '✗ FAIL'}")
    print(f"Конфликтов CAS: {conflicts}\n")
    return account.balance == 1000

# === Пример 2: STM MVCC ===

class TVar:
    def __init__(self, initial):
        self._value = initial
        self._version = 0
        self._lock = threading.Lock()

    def read(self):
        with self._lock:
            return self._value, self._version

    @property
    def value(self):
        with self._lock:
            return self._value

    def force_set(self, value, version):
        with self._lock:
            self._value = value
            self._version = version

class StmEngine:
    commit_lock = threading.Lock()

    @staticmethod
    def transfer(from_var, to_var, amount, on_conflict=None):
        attempts = 0
        while True:
            attempts += 1
            from_val, from_ver = from_var.read()
            to_val, to_ver = to_var.read()

            if from_val < amount:
                return False, attempts, "Недостаточно средств"

            new_from = from_val - amount
            new_to = to_val + amount

            time.sleep(random.uniform(0.0001, 0.0005))

            with StmEngine.commit_lock:
                cur_from_val, cur_from_ver = from_var.read()
                cur_to_val, cur_to_ver = to_var.read()
                if cur_from_ver == from_ver and cur_to_ver == to_ver:
                    from_var.force_set(new_from, from_ver+1)
                    to_var.force_set(new_to, to_ver+1)
                    return True, attempts, None

            if on_conflict:
                on_conflict()

def demo2():
    print("=== ДЕМО 2: STM Transfer MVCC ===")
    accA = TVar(1000)
    accB = TVar(500)
    sum_before = accA.value + accB.value
    conflicts = 0
    lock = threading.Lock()

    def worker(wid):
        nonlocal conflicts
        a_to_b = wid % 2 == 0
        from_var = accA if a_to_b else accB
        to_var = accB if a_to_b else accA

        def on_conflict():
            nonlocal conflicts
            with lock:
                conflicts += 1

        success, attempts, err = StmEngine.transfer(from_var, to_var, 20, on_conflict)
        if success:
            print(f"Поток {wid:2}: {'A→B' if a_to_b else 'B→A'} 20, попыток {attempts}")
        else:
            print(f"Поток {wid:2}: ОТКАЗ {err}")

    with ThreadPoolExecutor(max_workers=20) as ex:
        ex.map(worker, range(20))

    sum_after = accA.value + accB.value
    print(f"Итог: A={accA.value} B={accB.value} сумма до={sum_before} после={sum_after} {'✓ Инвариант сохранён' if sum_before==sum_after else '✗ FAIL'}")
    print(f"Конфликтов STM: {conflicts}\n")
    return sum_before == sum_after

if __name__ == "__main__":
    ok1 = demo1()
    ok2 = demo2()
    print(f"=== РЕЗУЛЬТАТ: {'ВСЁ OK' if ok1 and ok2 else 'ЕСТЬ ОШИБКИ'} ===")
