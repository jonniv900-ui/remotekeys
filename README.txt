CONTROLE REMOTO LAN 1.0 FINAL
=======================

Projeto Windows Forms em VB.NET para controlar as teclas direcionais,
Page Up, Page Down, Esc e Enter pelo navegador de um celular na mesma rede.

VERSÃO 1.0
- Primeira versão estável, encerrada em 2026.
- Ícone oficial integrado ao executável em múltiplas resoluções.
- © 2026 Wtec Sistemas.

COMPILAÇÃO
1. Abra ControleRemotoLAN.vbproj no Visual Studio.
2. Caso solicitado, instale o Developer Pack do .NET Framework 4.7.2.
3. Selecione Release e use Compilar > Compilar Solução.
4. O executável ficará em bin\Release\ControleRemotoLAN.exe.

COMO USAR
1. Execute o programa.
2. Clique em "Iniciar servidor".
3. Se o Firewall do Windows perguntar, permita acesso somente em redes privadas.
4. No celular conectado ao mesmo Wi-Fi, abra o endereço mostrado na janela.
5. No primeiro acesso, digite o PIN e toque em "Parear aparelho".
6. O navegador manterá o pareamento por cookie e não pedirá novamente o PIN.
7. Use os botões. Os direcionais repetem enquanto são segurados.
   Para jog no Mach3, a tecla permanece pressionada no PC até o botão ser solto no celular.
   Um sinal de manutenção é enviado a cada 500 ms e o servidor libera a tecla automaticamente após 1,5 segundo sem comunicação.

WEBAPP E MÚLTIPLOS APARELHOS
- A página é responsiva e possui aparência de aplicativo em celular e tablet.
- Cada celular recebe sua própria credencial de pareamento.
- Vários aparelhos podem ficar pareados e controlar o PC simultaneamente.
- O manifesto, os ícones e as tags de webapp permitem adicionar o controle à tela inicial.
- Android e iOS normalmente permitem "Adicionar à tela inicial" pelo menu do navegador.
- A instalação PWA completa e o funcionamento do service worker dependem de HTTPS,
  uma exigência dos navegadores; em HTTP local o controle continua funcionando normalmente.

ORGANIZAÇÃO, TOUCHPAD E TELA
- A ordem só pode ser alterada no aplicativo Windows.
- Arraste e solte as linhas do mapa ou use "Mover acima" e "Mover abaixo".
- A ordem é salva automaticamente no servidor e compartilhada por todos os aparelhos.
- A interface web não possui permissão nem comandos para reorganizar as teclas.
- Quando habilitados, Cima, Esquerda, Direita e Baixo permanecem sempre em forma de cruz.
- As demais teclas aparecem abaixo do bloco direcional, na ordem definida no aplicativo Windows.
- O touchpad virtual oferece movimento, clique, duplo clique, botão direito e rolagem.
- Para visualizar o monitor, marque "Permitir visualização da tela" no aplicativo Windows.
- Toque na miniatura da captura para abrir uma janela exclusiva de tela remota.
- Nessa janela, arraste sobre a imagem para mover o mouse do PC; a captura também mostra o cursor atual.
- Um toque curto sobre a imagem envia um clique esquerdo; arrastar não gera clique ao soltar.
- Use ⛶ para solicitar tela cheia ao navegador e × para fechar a janela.
- A tela é enviada somente a aparelhos pareados e a opção começa desativada.
- A captura não acessa a tela segura do UAC, a tela de login ou sessões Windows diferentes.

MAPA DE TECLAS
- O mapa inclui todas as teclas virtuais válidas do teclado Windows.
- Há doze botões dedicados, de Macro 1 a Macro 12, inicialmente desabilitados.
- No campo "Atalho / macro", use combinações como Ctrl+J, Ctrl+Shift+S, Alt+F4 ou Ctrl+Alt+End.
- A lista inclui permanentemente Ctrl+A até Ctrl+Z, Alt+A até Alt+Z, números, Ctrl/Alt com números e atalhos comuns do Windows.
- Quando a macro estiver vazia, o botão envia a tecla simples selecionada.
- Ctrl+Alt+Del é reservado pelo Windows e o aplicativo rejeita essa combinação com uma mensagem explicativa.
- As teclas Page Up e Page Down aparecem no seletor como PgUp e PgDn, sem os aliases Prior/Next do .NET.
- Na janela principal, escolha a tecla enviada por cada botão remoto.
- Marque "Habilitar tecla" somente nas teclas que deseja mostrar no celular.
- Edite "Nome no app" para escolher o texto exibido no botão.
- Na página web, o nome é desenhado como conteúdo visual não selecionável, evitando seleção de texto ao segurar o botão.
- Clique na célula "Cor" para abrir o seletor de cores do Windows.
- Qualquer alteração do mapa é salva automaticamente para a próxima inicialização.
- As oito teclas originais começam visíveis; as teclas adicionais começam ocultas.
- Clique em "Salvar mapa de teclas".
- Atualize a página do celular para carregar imediatamente o novo mapa.
- As escolhas, a visibilidade e a porta são mantidas entre execuções.

BANDEJA DO SISTEMA
- Minimizar ou fechar a janela mantém o aplicativo na bandeja.
- Dê duplo clique no ícone para reabrir.
- O menu do ícone permite abrir, iniciar/parar o servidor ou encerrar o aplicativo.
- Ícone cinza: servidor inativo.
- Ícone verde: servidor ativo.
- Ícone azul com raio amarelo: uma tecla acabou de ser enviada.

OBSERVAÇÕES
- Deixe no PC o programa que receberá as teclas em primeiro plano.
- O PIN é exigido apenas no pareamento inicial. Depois é usado um cookie HttpOnly.
- Porta, mapa, macros, aparência, ordem e pareamentos ficam em ControleRemotoLAN.config.xml, na pasta do executável.
- Se existir o antigo %%AppData%%\Wtec\ControleRemotoLAN\config.ini, ele é importado automaticamente para o XML na primeira execução.
- O arquivo INI antigo não é apagado durante a migração e pode ser mantido como cópia de segurança.
- O programa não usa serviços externos nem precisa de internet.
- O acesso pela internet exige configuração de roteador, firewall, IP público ou VPN e proteção adequada da conexão.
