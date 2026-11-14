#!/usr/bin/env python3
"""Draft a regional variant from a finished chunk, then print it for MANUAL review.

The output is only a starting point: always read every printed line and fix it (or put the
fixes in an overrides JSON, which is applied last and wins).

Usage:
  python3 tools/translation/derive_variant.py es2la work/Spanish_03.json work/SpanishLatin_03.json [overrides.json]
  python3 tools/translation/derive_variant.py br2pt work/PortugueseBrazilian_03.json work/Portuguese_03.json [overrides.json]
  python3 tools/translation/derive_variant.py zh2tw work/ChineseSimplified_03.json work/ChineseTraditional_03.json [overrides.json]
zh2tw needs: pip install opencc-python-reimplemented
Lines marked "*" differ from the source chunk.
"""
import json
import re
import sys

ES2LA = [
    (r'«([^»]*)»', r'“\1”'),
    (r'\bAñadir\b', 'Agregar'), (r'\bañadir\b', 'agregar'),
    (r'\bañadidos\b', 'agregados'), (r'\bañadidas\b', 'agregadas'), (r'\bañadido\b', 'agregado'), (r'\bañadida\b', 'agregada'),
    (r'\bañade\b', 'agrega'), (r'\bañaden\b', 'agregan'),
    (r'\bNo se ha podido\b', 'No se pudo'), (r'\bno se ha podido\b', 'no se pudo'),
    (r'\bNo se han podido\b', 'No se pudieron'), (r'\bno se han podido\b', 'no se pudieron'),
    (r'\bse ha conservado\b', 'se conservó'), (r'\bha fallado\b', 'falló'),
    (r'\bNo se han encontrado\b', 'No se encontraron'), (r'\bse ha realizado\b', 'se realizó'),
    # vanilla SpanishLatin terminology
    (r'\bnarrador\b', 'cuentacuentos'), (r'\bNarrador\b', 'Cuentacuentos'),
    (r'\bpsienlace\b', 'psicoenlace'),
    (r'\bmecanitor(es)?\b', lambda m: 'mecanizador' + (m.group(1) or '')),
    (r'\bmecaconglomerados?\b', lambda m: 'clústeres de mecas' if m.group(0).endswith('s') else 'clúster de mecas'),
    (r'\breputación\b', 'buena voluntad'), (r'\bobra maestra\b', 'magistral'),
    (r'\bordenador\b', 'computadora'),
    (r'\brecogida de datos\b', 'recopilación de datos'), (r'\bRecogida de datos\b', 'Recopilación de datos'),
    (r'\bAportación\b', 'Contribución'), (r'\baportación\b', 'contribución'),
]

BR2PT = [
    (r'“([^”]*)”', r'«\1»'),
    (r'\bjogos salvos\b', 'jogos guardados'), (r'\bjogo salvo\b', 'jogo guardado'),
    (r'\bJogos salvos\b', 'Jogos guardados'), (r'\bJogo salvo\b', 'Jogo guardado'),
    (r'\bo jogo foi salvo\b', 'o jogo foi guardado'), (r'\bcomo salvo\b', 'tal como guardado'),
    (r'\bsalvamentos?\b', lambda m: 'gravações' if m.group(0).endswith('s') else 'gravação'),
    (r'\bsalvar\b', 'guardar'), (r'\bSalvar\b', 'Guardar'), (r'\bsalva\b', 'guarda'),
    (r'\bsalvo em\b', 'guardado em'), (r'\bsalvos\b', 'guardados'), (r'\bsalvo\b', 'guardado'),
    (r'\bcolônias\b', 'colónias'), (r'\bColônias\b', 'Colónias'), (r'\bcolônia\b', 'colónia'), (r'\bColônia\b', 'Colónia'),
    (r'\barquivos\b', 'ficheiros'), (r'\bArquivos\b', 'Ficheiros'), (r'\barquivo\b', 'ficheiro'), (r'\bArquivo\b', 'Ficheiro'),
    (r'\btela\b', 'ecrã'), (r'\bTela\b', 'Ecrã'),
    (r'\bregistrad([oa]s?)\b', r'registad\1'), (r'\bRegistrad([oa]s?)\b', r'Registad\1'),
    (r'\bregistrar\b', 'registar'), (r'\bregistra\b', 'regista'), (r'\bregistram\b', 'registam'),
    (r'\bregistros\b', 'registos'), (r'\bregistro\b', 'registo'), (r'\bRegistro\b', 'Registo'),
    (r'\bcoleta\b', 'recolha'), (r'\bColeta\b', 'Recolha'),
    (r'\bIsso\b', 'Isto'), (r'\bisso\b', 'isto'), (r'\bdesse\b', 'deste'), (r'\bdessa\b', 'desta'),
    (r'\bSomente\b', 'Apenas'), (r'\bsomente\b', 'apenas'),
    (r'\bvocê\b', ''), (r'\bVocê\b', ''),
    (r'\busuários?\b', lambda m: 'utilizadores' if m.group(0).endswith('s') else 'utilizador'),
    (r'\bequipe\b', 'equipa'), (r'\bdetectad([oa]s?)\b', r'detetad\1'),
    (r'\bExportando\b', 'A exportar'), (r'\bCarregando\b', 'A carregar'), (r'\bProcessando\b', 'A processar'),
    (r'\bem um\b', 'num'), (r'\bem uma\b', 'numa'),
    (r'\bAté o\b', 'Até ao'), (r'\baté o\b', 'até ao'), (r'\baté a\b', 'até à'),
    (r'\bgerenciar\b', 'gerir'), (r'\bGerenciar\b', 'Gerir'), (r'\bgerenciamento\b', 'gestão'),
    (r'\bcontato\b', 'contacto'), (r'\bfato\b', 'facto'),
    (r'\bArquivo ausente\b', 'Ficheiro em falta'), (r'\bficheiro ausente\b', 'ficheiro em falta'),
    (r'\bmodo desenvolvedor\b', 'modo de desenvolvimento'),
    (r', então ', ', pelo que '),
    (r'  +', ' '),
]

# Applied after OpenCC s2twp (left side already in Traditional characters).
ZH2TW = [
    # RimWorld zh-TW official terminology
    ('超凡樞紐', '遠古聯繫'), ('主腦核心', '超智腦核心'), ('榮譽晉升', '榮譽提升'),
    ('遠行隊', '旅隊'), ('機械師', '御機使'), ('逆重飛船', '重力船'), ('好感度', '友好度'),
    ('劇本', '腳本'), ('啟靈神經', '心靈連結'), ('炮塔', '砲塔'), ('囚犯', '俘虜'),
    ('有毒塵埃', '毒霧霾'), ('文化', '理念'), ('機械培育器', '機械培養艙'),
    ('垃圾分解器', '廢料袋原子化機器'), ('有毒垃圾包', '廢料袋'), ('有毒垃圾', '廢料袋'),
    ('水栽培植物盆', '水耕植物盆'), ('低溫休眠艙', '休眠艙'), ('獵殺人類的獸群', '狂亂獸群'),
    ('機械集群', '機械軍團'), ('太陽耀斑', '太陽閃焰'), ('伏擊', '埋伏'), ('打架', '吵架'),
    ('牽絆', '羈絆'), ('極差', '糟糕'), ('大師級', '大師'), ('傳奇級', '傳奇'),
    ('高級零部件', '高級零件'), ('零部件', '零件'), ('蓄電池', '電池'), ('人格武器', '羈絆武器'),
    ('哨所', '哨站'), ('生態群落', '生態系統'), ('治療質量', '治療品質'), ('質量', '品質'), ('臺賬', '台帳'),
    # Taiwan usage fixes for OpenCC output (NOTE: mainland 列=column/行=row, Taiwan 欄=column/列=row)
    ('型別', '類型'), ('“', '「'), ('”', '」'), ('‘', '『'), ('’', '』'),
    ('只讀', '唯讀'), ('標籤頁', '分頁'), ('日誌', '記錄檔'), ('識別', '辨識'), ('丟失', '遺失'),
    ('跳過', '略過'), ('原始檔', '來源檔案'), ('列表', '清單'), ('對比', '比較'),
    ('分組', '群組'), ('詳情', '詳細資訊'), ('新增列', '新增欄'), ('移除列', '移除欄'),
    ('每一行', '每一列'), ('這些行', '這些列'), ('標題介面', '標題畫面'), ('適應螢幕', '符合螢幕'),
    ('更新版本', '較新版本'), ('檢視記錄檔', '參閱記錄檔'), ('單個', '單一'), ('源資料', '來源資料'), ('匹配', '符合'),
]


def regex_convert(rules, text):
    lead = text.startswith(' ')
    for pat, rep in rules:
        text = re.sub(pat, rep, text)
    return (' ' if lead else '') + text.strip()


def main():
    if len(sys.argv) < 4 or sys.argv[1] not in ('es2la', 'br2pt', 'zh2tw'):
        sys.exit(__doc__)
    mode, src, dst = sys.argv[1:4]
    source = json.load(open(src, encoding='utf-8'))
    if mode == 'zh2tw':
        from opencc import OpenCC
        cc = OpenCC('s2twp')

        def convert(text):
            out = cc.convert(text)
            for a, b in ZH2TW:
                out = out.replace(a, b)
            return out
    else:
        rules = ES2LA if mode == 'es2la' else BR2PT

        def convert(text):
            return regex_convert(rules, text)
    result = {k: convert(v) for k, v in source.items()}
    if len(sys.argv) > 4:
        result.update(json.load(open(sys.argv[4], encoding='utf-8')))
    json.dump(result, open(dst, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
    for k, v in result.items():
        print(f'{"*" if v != source.get(k) else " "} {k}: {v}')


if __name__ == '__main__':
    main()
