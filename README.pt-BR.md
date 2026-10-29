<p align="center"><img src="assets/logo.svg" alt="Nostalgic Rellm" width="720"></p>

# Nostalgic Rellm

*[Read in English](README.md)*

Monitor de hardware leve e com skins para Windows: uma barra horizontal translúcida que fica sobre a
barra de tarefas, inspirada no gerenciador de janelas Enlightenment (E16/E17) e no clássico GKrellM.

## Sobre

Este projeto existe porque alguém sente saudade da era de ouro dos desktops Linux, quando escolher um tema
do Enlightenment levava mais tempo que o trabalho em si, o GKrellM piscava LEDzinhos para você no canto da
tela e tudo era translúcido, chanfrado e só levemente legível. Era glorioso.

Aí o mundo descobriu o design flat. Muito limpo. Muito eficiente. Muito bege.

Então aqui está um monitor de hardware que se recusa a crescer. Ele não vai mudar sua vida, turbinar sua
produtividade nem alinhar suas sinergias. Ele só mostra o uso da sua CPU dentro de uma barra de vidro com
um LED aceso, do jeito que a natureza mandou. Se você procurava uma solução de monitoramento séria e
corporativa, errou a saída lá por 2004.

- CPU % e temperatura · RAM % · GPU % e temperatura · VRAM % · espaço ocupado de cada disco (rótulo + letra)
- Gráficos de histórico estilo GKrellM (liga e desliga no menu)
- Clique em CPU, RAM, GPU ou VRAM: popup translúcido com os 5 processos que mais consomem; ✕ duas vezes finaliza
- Botão **IA**: uso da sessão (5 h) e semanal do Claude e do GPT (Codex)
- Dois LEDs estilo GKrellM: recolher até ficarem só os LEDs, e ancorar no topo da tela reservando a faixa
  (janelas maximizadas começam abaixo dela)
- Some sozinha durante jogos, vídeos e apresentações em tela cheia
- Skins em JSON, trocadas pelo menu do botão direito; inglês e português do Brasil, com arquivos de tradução
- Exe único e portátil, sem ícone na bandeja; ~0,1% de CPU e ~50 MB de memória privada

## Download

A página de Releases tem duas versões:

| Versão | Tamanho | Precisa de |
|---|---|---|
| `NostalgicRellm.exe` | ~5 MB | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) instalado |
| `NostalgicRellm-full.exe` | ~50 MB | nada, o .NET vai junto |

O exe não tem assinatura digital, então o SmartScreen do Windows mostra o aviso de "aplicativo não
reconhecido" na primeira vez (Mais informações → Executar assim mesmo). Como ele pede administrador e lê
sensores de baixo nível, alguns antivírus podem desconfiar; o código está todo aqui para conferir.

## Requisitos

- Windows 10/11 x64
- A barra se reabre como administrador (necessário para a temperatura da CPU e para finalizar processos de
  outros usuários). `--no-elevate` abre sem pedir, só que sem a temperatura da CPU.
- **Temperatura da CPU:** também precisa do driver [PawnIO](https://pawnio.eu). Sem ele, a CPU mostra
  `--°C` e todo o resto funciona.

## Uso

| Ação | Efeito |
|---|---|
| Arrastar | Move a barra solta (gruda nas bordas de cima e de baixo da tela principal) |
| Clique em CPU/RAM/GPU/VRAM | Abre ou fecha o top 5 de processos |
| Clique em IA | Abre o uso do Claude e do GPT |
| LED de cima | Recolhe até os LEDs ↔ expande |
| LED de baixo | Ancora no topo da tela ↔ volta a ficar solta |
| Botão direito | Skin, gráficos, recolhida, ancorada, Conexões…, idioma, pasta de skins, sair |

Posição e preferências ficam em `settings.json`, ao lado do exe. `"Height": 0` segue a altura da barra de tarefas.

## Uso das IAs

Ninguém digita senha nem token em lugar nenhum. Botão direito → **Conexões…** mostra a situação de cada uma.

- **Claude** vem da [linha de status](https://code.claude.com/docs/en/statusline) oficial do Claude Code.
  **Conectar** acrescenta isto ao `~/.claude/settings.json` (perguntando antes, e avisando se já houver
  uma linha de status):

  ```json
  "statusLine": { "type": "command", "command": "\"C:/caminho/NostalgicRellm.exe\" --statusline" }
  ```

  A cada atualização, o Claude Code entrega o uso da sessão e da semana (planos Pro e Max); o monitor guarda
  a última leitura em `%LOCALAPPDATA%\NostalgicRellm\claude-usage.json`. Os números valem para o plano
  inteiro (app, site e terminal), mas só atualizam enquanto o Claude Code roda no terminal. Nenhuma
  credencial do Claude é lida.
- **GPT** usa o login que o app do Codex já salvou em `~/.codex/auth.json` e mostra os limites do Codex
  (não os do chat do ChatGPT). O endereço não é documentado e pode mudar. O token só é lido, nunca renovado
  pelo monitor, vai só para a OpenAI e só enquanto o popup de IAs está aberto.

## Skins

Cada skin é uma pasta em `skins/` com um `skin.json` (cores `#RRGGBB` ou `#AARRGGBB`); campos omitidos
usam o padrão. Vêm prontas: E17 Glass, E16 Bevel, GKrellM Classic e Minimal. Uma imagem PNG de fundo
opcional é definida com `"backgroundImage": "fundo.png"`.

## Traduções

Os idiomas embutidos ficam em `src/lang/<código>.json` (chave → texto). Para criar ou ajustar um idioma sem
recompilar, coloque um `lang/<código>.json` ao lado do exe: ele substitui o embutido de mesmo código ou
acrescenta um idioma novo ao menu. Chaves que faltarem caem no inglês.

## Compilar

```
cd src
dotnet publish -c Release -o ../publish
dotnet publish -c Release -o ../publish-full --self-contained true
```

Diagnóstico dos sensores (gera `dump.txt` ao lado do exe):

```
NostalgicRellm.exe --dump
```

## Licença

[0BSD](LICENSE): faça o que quiser, sem precisar citar ninguém.

Os sensores são lidos com a [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
(MPL-2.0), que vai embutida no exe; o código-fonte dela está nesse link.
