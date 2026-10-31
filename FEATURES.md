# Features pendentes

Ideias para próximas versões do Nostalgic Rellm, na ordem em que foram surgindo. Cada uma tem um status:

- **Ideia:** anotada, ainda sem decisão de como fazer
- **Planejada:** caminho definido, pronta para implementar
- **Em andamento** / **Feita (versão)**

---

## 1. Notas e lembretes

**Status:** Ideia · **Pedido em:** 2026-10-30

Um botão mínimo na barra (no estilo dos LEDs/célula de IA) que abre um popup translúcido para anotar e guardar
notas e lembretes rápidos, sem sair do que se está fazendo.

**O que se espera**
- Botão simples na barra; clique abre o popup, clique de novo (ou Esc) fecha
- Criar, editar, marcar como feito e apagar notas
- Lembretes com data e hora, que avisam na hora certa
- **Ideal:** ver e editar as mesmas notas no celular

**Sincronização com o celular: opções pesquisadas**

| Opção | Conta pessoal? | No celular | Observações |
|---|---|---|---|
| Google Keep | Não | App Keep | A API só funciona para contas corporativas (Google Workspace); com Gmail pessoal dá erro de permissão. Descartado. |
| **Google Tasks** | Sim | App Google Tasks + Google Agenda | API aberta para Gmail pessoal. Pela API, o prazo é só a data (sem hora); os avisos no celular vêm do app Tasks/Agenda. Encaixa no ecossistema Google que já se usa. |
| Microsoft To Do | Sim | App To Do | API (Microsoft Graph) aceita lembrete com hora exata e avisa no celular. Exige conta Microsoft. |
| Só local | — | Não | Mais simples e privado; o aviso com hora fica a cargo do próprio monitor (notificação do Windows). |

**Proposta inicial (a decidir)**
- Notas guardadas localmente no PC, para funcionar mesmo sem internet.
- Sincronização opcional com o **Google Tasks**: login uma única vez pelo navegador (OAuth do Google, sem
  digitar senha no monitor), cada nota vira uma tarefa numa lista "Nostalgic Rellm".
- Lembrete com hora: o monitor mostra a notificação do Windows na hora exata; no celular, o aviso vem pela
  data da tarefa no Google Tasks.

**Perguntas em aberto**
- Google Tasks está bom, ou o celular é iPhone/usa outro app de lembretes?
- Notas precisam de formatação (listas, negrito) ou texto simples basta?
- O aviso do lembrete deve ser só a notificação do Windows ou também piscar o LED / o botão na barra?

---

<!-- Próximas ideias entram abaixo, no mesmo formato. -->
