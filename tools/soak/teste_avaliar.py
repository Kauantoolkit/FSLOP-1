#!/usr/bin/env python3
"""
Teste do avaliador. Roda com:  python teste_avaliar.py

Existe por um motivo so: um avaliador que nunca foi visto REPROVANDO nao foi
verificado, foi apenas executado. Cada caso abaixo quebra uma coisa de
proposito e exige que o avaliador aponte exatamente aquela metrica — nem outra,
nem nenhuma.

O caso `base` e o inverso: tudo certo, e o avaliador nao pode reprovar nada.
Sem ele, um avaliador que reprovasse tudo tambem passaria em todos os outros.

Stdlib apenas.
"""

import os
import shutil
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import avaliar as av  # noqa: E402

HASH = "a1b2c3d4"
N_AMOSTRAS = 11
T0 = 296.0          # comeca antes dos 300 s para o corte de drift ser exercido
TICK0 = 17760
TICK_LATE = TICK0 + 2 * 60


def meta(role, ident, **over):
    campos = {
        "run": "r001", "stack": "B", "role": role, "id": str(ident), "pid": "1000",
        "build": "abc1234", "engine": "6000.6.0f1", "transport": "local",
        "rtt_ms": "150", "loss_pct": "3.0", "bodies": "150",
        "started": "2026-09-06T03:00:00Z",
    }
    campos.update(over)
    return "[SOAK-META] " + " ".join("%s=%s" % kv for kv in campos.items())


def amostra(role, ident, i, **over):
    campos = {
        "t": "%.3f" % (T0 + i), "tick": str(TICK0 + i * 60), "role": role, "id": str(ident),
        "fps": "62.0", "frame_p99_ms": "15.00",
        "rx_KBps": "18.5", "tx_KBps": "4.2",
        "drift_max_u": ("-1" if role == "host" else "0.0200"),
        "drift_p99_u": ("-1" if role == "host" else "0.0150"),
        "carry_jump_u": "0.020", "input_ms_p99": ("-1" if role == "host" else "40.0"),
        "bodies_awake": "12", "world_hash": HASH,
    }
    campos.update(over)
    return "[SOAK] " + " ".join("%s=%s" % kv for kv in campos.items())


def evento(role, ident, ev, tick, t, **extras):
    campos = {"t": "%.3f" % t, "tick": str(tick), "role": role, "id": str(ident), "ev": ev}
    campos.update(extras)
    return "[SOAK-EV] " + " ".join("%s=%s" % kv for kv in campos.items())


def montar_base():
    """Devolve {nome_do_arquivo: [linhas]} de uma corrida que deve passar."""
    host = [meta("host", 0)]
    cliente = [meta("client", 1)]
    for i in range(N_AMOSTRAS):
        host.append(amostra("host", 0, i))
        cliente.append(amostra("client", 1, i))
    cliente.append(
        evento("client", 1, "late_join_done", TICK_LATE, T0 + 2,
               elapsed_ms="820.0", world_hash=HASH)
    )
    host.append(evento("host", 0, "host_quit", TICK0 + 10 * 60, T0 + 10))
    cliente.append(
        evento("client", 1, "shutdown", TICK0 + 10 * 60, T0 + 10, clean="1", reason="host_lost")
    )
    return {"inst0-host.log": host, "inst1-client.log": cliente}


def gravar(arquivos):
    pasta = tempfile.mkdtemp(prefix="soaktest-")
    for nome, linhas in arquivos.items():
        with open(os.path.join(pasta, nome), "w", encoding="utf-8") as fh:
            fh.write("\n".join(linhas) + "\n")
    return pasta


def rodar(arquivos):
    pasta = gravar(arquivos)
    try:
        codigo, resultados = av.avaliar(pasta, instancias=2, duracao_minima=5.0)
        return codigo, {r.nome: r for r in resultados}
    finally:
        shutil.rmtree(pasta, ignore_errors=True)


# --------------------------------------------------------------------------
# Casos
# --------------------------------------------------------------------------

def caso_base():
    codigo, res = rodar(montar_base())
    reprovadas = [n for n, r in res.items() if r.status == "FAIL"]
    return (codigo == 0 and not reprovadas,
            "codigo=%d reprovadas=%s" % (codigo, reprovadas))


def espera_falha(arquivos, metrica):
    codigo, res = rodar(arquivos)
    if codigo != 1:
        return False, "esperava codigo 1, veio %d" % codigo
    if metrica not in res:
        return False, "metrica '%s' nem foi avaliada (avaliadas: %s)" % (
            metrica, sorted(res))
    if res[metrica].status != "FAIL":
        return False, "'%s' veio %s: %s" % (metrica, res[metrica].status, res[metrica].detalhe)
    return True, "%s: %s" % (metrica, res[metrica].detalhe)


def caso_pasta_vazia():
    return espera_falha({}, "integridade")


def caso_sem_meta():
    a = montar_base()
    a["inst1-client.log"] = [l for l in a["inst1-client.log"] if not l.startswith("[SOAK-META]")]
    return espera_falha(a, "integridade")


def caso_sem_amostras():
    a = montar_base()
    for nome in a:
        a[nome] = [l for l in a[nome] if not l.startswith("[SOAK] ")]
    return espera_falha(a, "integridade")


def caso_dois_hosts():
    a = montar_base()
    a["inst1-client.log"][0] = meta("host", 1)
    return espera_falha(a, "integridade")


def caso_runs_misturadas():
    a = montar_base()
    a["inst1-client.log"][0] = meta("client", 1, run="r999")
    return espera_falha(a, "integridade")


def caso_input_nao_instrumentado():
    """-1 nao pode passar por "respondeu em -1 ms".

    Falso verde real de 17/09: o cliente emitia input_ms_p99=-1 porque o campo nao
    estava instrumentado, e o avaliador devolvia PASS com "pior p99 -1.0 ms
    (limite 100)". O juiz dava verde justamente para o que nao foi medido.
    """
    a = montar_base()
    a["inst1-client.log"] = [meta("client", 1)] + [
        amostra("client", 1, i, input_ms_p99="-1") for i in range(N_AMOSTRAS)
    ] + [
        evento("client", 1, "late_join_done", TICK_LATE, T0 + 2,
               elapsed_ms="820.0", world_hash=HASH),
        evento("client", 1, "shutdown", TICK0 + 10 * 60, T0 + 10, clean="1", reason="x"),
    ]
    return espera_falha(a, "resposta_do_input")


def caso_drift_nao_instrumentado():
    """Mesma armadilha do input, no campo de drift."""
    a = montar_base()
    a["inst1-client.log"] = [meta("client", 1)] + [
        amostra("client", 1, i, drift_max_u="-1", drift_p99_u="-1")
        for i in range(N_AMOSTRAS)
    ] + [
        evento("client", 1, "late_join_done", TICK_LATE, T0 + 2,
               elapsed_ms="820.0", world_hash=HASH),
        evento("client", 1, "shutdown", TICK0 + 10 * 60, T0 + 10, clean="1", reason="x"),
    ]
    return espera_falha(a, "drift")


def caso_relogio_travado():
    """O processo parou de simular no meio, mas seguiu escrevendo amostras.

    Caso real (errors/05 da task): o player do Unity para o loop quando a janela
    perde o foco. As amostras que restam sao todas BOAS -- e por isso a corrida
    passava em tudo. Aqui os ticks param de avancar a partir da 5a amostra
    enquanto t continua andando.
    """
    a = montar_base()
    a["inst0-host.log"] = [meta("host", 0)] + [
        amostra("host", 0, i, tick=str(TICK0 + min(i, 4) * 60))
        for i in range(N_AMOSTRAS)
    ] + [evento("host", 0, "host_quit", TICK0 + 4 * 60, T0 + 10)]
    return espera_falha(a, "relogio_coerente")


def caso_duracao_no_limite():
    """Corrida que cobre exatamente o minimo nao pode reprovar por 1 amostra.

    Cada amostra cobre o intervalo ate a proxima, entao 11 amostras de 1 em 1 s
    cobrem 11 s e nao 10. Antes de 17/09 o avaliador media so o span e reprovava
    a corrida de 600 s exatos do soak.
    """
    codigo, res = rodar(montar_base())
    r = res.get("duracao")
    if r is None:
        return False, "duracao nem foi avaliada"
    # montar_base tem N_AMOSTRAS amostras de 1 em 1 s: cobre N_AMOSTRAS segundos.
    ok = r.status == "PASS"
    return ok, "duracao: %s (%s)" % (r.status, r.detalhe)


def caso_duracao_curta():
    a = {"inst0-host.log": [meta("host", 0), amostra("host", 0, 0), amostra("host", 0, 1),
                            evento("host", 0, "host_quit", TICK0, T0)],
         "inst1-client.log": [meta("client", 1), amostra("client", 1, 0), amostra("client", 1, 1),
                              evento("client", 1, "shutdown", TICK0, T0, clean="1", reason="x")]}
    return espera_falha(a, "duracao")


def caso_fps_baixo():
    a = montar_base()
    a["inst0-host.log"] = [meta("host", 0)] + [
        amostra("host", 0, i, fps="41.0") for i in range(N_AMOSTRAS)
    ] + [evento("host", 0, "host_quit", TICK0 + 600, T0 + 10)]
    return espera_falha(a, "fps_host")


def caso_frame_p99_alto():
    """fps medio bate 60 mas o quadro engasga — 'estavel' tem que pegar isso."""
    a = montar_base()
    a["inst0-host.log"] = [meta("host", 0)] + [
        amostra("host", 0, i, fps="60.5", frame_p99_ms="48.00") for i in range(N_AMOSTRAS)
    ] + [evento("host", 0, "host_quit", TICK0 + 600, T0 + 10)]
    return espera_falha(a, "fps_host")


def caso_teleporte():
    a = montar_base()
    a["inst1-client.log"][5] = amostra("client", 1, 4, carry_jump_u="0.940")
    return espera_falha(a, "viga_nao_teleporta")


def caso_campo_ausente():
    """Campo que sumiu tem que virar FAIL legivel, nunca stack trace nem PASS."""
    a = montar_base()
    novas = []
    for l in a["inst1-client.log"]:
        if l.startswith("[SOAK] "):
            l = " ".join(t for t in l.split() if not t.startswith("carry_jump_u="))
        novas.append(l)
    a["inst1-client.log"] = novas
    a["inst0-host.log"] = [
        " ".join(t for t in l.split() if not t.startswith("carry_jump_u="))
        if l.startswith("[SOAK] ") else l
        for l in a["inst0-host.log"]
    ]
    return espera_falha(a, "viga_nao_teleporta")


def caso_input_lento():
    a = montar_base()
    a["inst1-client.log"][3] = amostra("client", 1, 2, input_ms_p99="163.0")
    return espera_falha(a, "resposta_do_input")


def caso_drift_alto():
    a = montar_base()
    # i=8 -> t=304, ja passou dos 300 s: entra no recorte da metrica
    a["inst1-client.log"][9] = amostra("client", 1, 8, drift_max_u="0.3100")
    return espera_falha(a, "drift")


def caso_drift_antes_dos_300s_nao_conta():
    """Guarda contra o avaliador ser severo demais: drift alto ANTES dos 5 min
    e permitido pelo briefing, e reprovar ali seria falso positivo."""
    a = montar_base()
    a["inst1-client.log"][1] = amostra("client", 1, 0, drift_max_u="0.9000")  # t=296
    codigo, res = rodar(a)
    ok = res.get("drift") is not None and res["drift"].status == "PASS"
    return ok, "drift=%s (codigo %d)" % (
        res["drift"].status if "drift" in res else "ausente", codigo)


def caso_late_join_divergente():
    a = montar_base()
    a["inst1-client.log"] = [
        evento("client", 1, "late_join_done", TICK_LATE, T0 + 2,
               elapsed_ms="820.0", world_hash="deadbeef")
        if "late_join_done" in l else l
        for l in a["inst1-client.log"]
    ]
    return espera_falha(a, "late_join")


def caso_late_join_sem_tick_no_host():
    a = montar_base()
    a["inst1-client.log"] = [
        evento("client", 1, "late_join_done", 999999, T0 + 2,
               elapsed_ms="820.0", world_hash=HASH)
        if "late_join_done" in l else l
        for l in a["inst1-client.log"]
    ]
    return espera_falha(a, "late_join")


def caso_sem_host_quit():
    a = montar_base()
    a["inst0-host.log"] = [l for l in a["inst0-host.log"] if "host_quit" not in l]
    return espera_falha(a, "queda_do_host")


def caso_shutdown_sujo():
    a = montar_base()
    a["inst1-client.log"] = [
        evento("client", 1, "shutdown", TICK0 + 600, T0 + 10,
               clean="0", reason="NullReferenceException")
        if "ev=shutdown" in l else l
        for l in a["inst1-client.log"]
    ]
    return espera_falha(a, "queda_do_host")


def caso_cliente_calado_no_shutdown():
    a = montar_base()
    a["inst1-client.log"] = [l for l in a["inst1-client.log"] if "ev=shutdown" not in l]
    return espera_falha(a, "queda_do_host")


def caso_excecao():
    a = montar_base()
    a["inst1-client.log"].append(
        evento("client", 1, "exception", TICK0 + 300, T0 + 5, where="CarryJoint.Update")
    )
    return espera_falha(a, "zero_excecoes")


CASOS = [
    ("base passa limpo", caso_base),
    ("pasta vazia", caso_pasta_vazia),
    ("log sem cabecalho META", caso_sem_meta),
    ("nenhuma amostra", caso_sem_amostras),
    ("dois hosts", caso_dois_hosts),
    ("logs de corridas diferentes", caso_runs_misturadas),
    ("corrida curta demais", caso_duracao_curta),
    ("input nao instrumentado (-1)", caso_input_nao_instrumentado),
    ("drift nao instrumentado (-1)", caso_drift_nao_instrumentado),
    ("relogio travado no meio", caso_relogio_travado),
    ("duracao exatamente no limite", caso_duracao_no_limite),
    ("fps do host abaixo de 60", caso_fps_baixo),
    ("fps ok mas p99 do quadro estourado", caso_frame_p99_alto),
    ("viga teleporta", caso_teleporte),
    ("campo carry_jump_u ausente", caso_campo_ausente),
    ("input acima de 100 ms", caso_input_lento),
    ("drift acima de 0.15 depois dos 5 min", caso_drift_alto),
    ("drift alto ANTES dos 5 min nao reprova", caso_drift_antes_dos_300s_nao_conta),
    ("late join com estado divergente", caso_late_join_divergente),
    ("late join sem tick correspondente no host", caso_late_join_sem_tick_no_host),
    ("host nunca emitiu host_quit", caso_sem_host_quit),
    ("cliente encerrou com clean=0", caso_shutdown_sujo),
    ("cliente nao emitiu shutdown", caso_cliente_calado_no_shutdown),
    ("uma excecao na corrida", caso_excecao),
]


def main():
    largura = max(len(n) for n, _ in CASOS)
    passou = 0
    for nome, fn in CASOS:
        try:
            ok, detalhe = fn()
        except Exception as erro:  # noqa: BLE001 — o teste nunca pode explodir
            ok, detalhe = False, "EXCECAO no proprio teste: %r" % erro
        print("%-4s %-*s  %s" % ("ok" if ok else "FALHOU", largura, nome, detalhe))
        passou += 1 if ok else 0
    print("-" * 78)
    print("%d/%d" % (passou, len(CASOS)))
    return 0 if passou == len(CASOS) else 1


if __name__ == "__main__":
    sys.exit(main())
