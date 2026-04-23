# 🔐 MySSH — Customizable SSH Client

Cliente SSH e SFTP personalizável para Windows, desenvolvido em **C# .NET 8 (Windows Forms)**.

---

## ✨ Funcionalidades

### 🖥️ Terminal SSH Completo
- Emulador de terminal baseado em **xterm.js** + **WebView2**
- Suporte total a sequências ANSI/VT100 (cores, cursor, teclas de seta)
- Compatível com editores interativos como `nano`, `vim` e ferramentas como `htop`
- Cores e tema dark integrados

### ⚙️ Aba de Configurações
- Campos para **Host / IP**, **Usuário** e **Senha**
- Senha armazenada com **criptografia DPAPI** (nativa do Windows — apenas o seu usuário consegue descriptografar)
- Configurações salvas automaticamente ao conectar e ao fechar o app

### 📁 Aba SFTP
- Painel dividido: **local** (esquerda) e **remoto** (direita)
- Navegação por pastas com duplo clique e entrada manual de caminho (pressione Enter para navegar)
- **Upload** de arquivos locais para o servidor remoto
- **Download** de arquivos remotos para a máquina local
- Última pasta local e remota são **lembradas automaticamente** entre sessões

### ⚡ Ações Rápidas (Floating Button)
- Botão flutuante (FAB) estilizado no canto inferior direito do terminal
- Clique no **`+`** para abrir um menu de ações customizadas
- Cada ação executa um **comando SSH pré-definido** com um único clique (Enter enviado automaticamente)
- Ações configuradas em uma aba dedicada (**Ações Rápidas**) com tabela editável
- Salvas automaticamente ao editar — sem necessidade de clicar em "Salvar"

---

## 🚀 Como executar

### Pré-requisitos
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- Windows 10 (atualizado) ou Windows 11
- WebView2 Runtime (já incluído no Windows 11; no Windows 10 é instalado automaticamente pelo Edge)

### Rodando localmente

```bash
cd C:\GitHub\MySSH
dotnet run
```

### Compilando para distribuição

```bash
dotnet publish -c Release -r win-x64 --self-contained true -o ./publish
```

---

## 🏗️ Arquitetura

```
MySSH/
├── Program.cs           # Ponto de entrada
├── MainForm.cs          # Interface principal (TabControl com 4 abas)
├── SshManager.cs        # Gerencia conexões SSH e SFTP (SSH.NET)
├── ConfigManager.cs     # Leitura/escrita do config.json com DPAPI
├── Resources/
│   └── terminal.html    # Emulador de terminal (xterm.js + FAB)
└── config.json          # Gerado em runtime — armazena configurações
```

### Dependências NuGet

| Pacote | Uso |
|--------|-----|
| `SSH.NET` | Conexão SSH, ShellStream e transferências SFTP |
| `Microsoft.Web.WebView2` | Hospeda o xterm.js dentro do Windows Forms |
| `Newtonsoft.Json` | Serialização do `config.json` |
| `System.Security.Cryptography.ProtectedData` | Criptografia DPAPI da senha |

---

## 🔒 Segurança

A senha é **nunca** salva em texto puro. O processo de persistência usa a API `ProtectedData.Protect()` do Windows com escopo `CurrentUser`, garantindo que o valor criptografado só possa ser recuperado pelo mesmo usuário do Windows na mesma máquina.

---

## 💡 Exemplos de Ações Rápidas

| Nome da Ação | Comando |
|---|---|
| Listar Arquivos | `ls -la` |
| Abrir Inetpub | `cd /var/www/html` |
| Ver logs do Nginx | `tail -f /var/log/nginx/error.log` |
| Status dos serviços | `systemctl status` |
| Uso de disco | `df -h` |

---

## 📋 Requisitos de sistema

- **OS:** Windows 10 (build 1903+) ou Windows 11
- **Runtime:** .NET 8
- **WebView2:** Incluído no Windows 11 / Edge atualizado no Windows 10
