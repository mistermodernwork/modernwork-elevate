# ModernWork Elevate — Anleitung

> *[This guide in English](user-guide.md)*
>
> Für alle, die die App benutzen. Das Einrichten für einen Tenant ist Admin-Arbeit und steht in
> [app-registration-setup.md](app-registration-setup.md) und
> [unattended-deployment.md](unattended-deployment.md).
>
> Die Oberfläche der App ist englisch. Bezeichnungen wie **Activate on** oder **Justification**
> stehen deshalb auch hier auf Englisch — so wie du sie im Fenster siehst.

## Wozu die App da ist

Deine Admin-Rollen sind nicht dauerhaft eingeschaltet. Microsoft Entra Privileged Identity
Management (PIM) gibt sie auf Anforderung heraus, für ein paar Stunden, damit ein Konto, das
kompromittiert wird während du nicht arbeitest, kein Admin-Konto ist. Das Einschalten heißt
**aktivieren**.

Im Azure-Portal bedeutet das: Browser öffnen, PIM-Blade suchen, sich durch mehrere Seiten klicken.
Diese App macht dasselbe über ein Symbol neben der Uhr.

**Sie kann dir nichts geben, was du nicht schon hast.** Sie aktiviert Rollen, für die du *eligible*
bist — die dir also vorher jemand zugewiesen hat. Fehlt eine Rolle, die du erwartest, ist das nicht
hier zu beheben: frag die Administration deines Tenants.

## Installieren

Deine IT installiert die App für das ganze Gerät (ein MSI, meist über Intune oder Configuration
Manager). Sie liegt unter `C:\Program Files\ModernWork Elevate`, mit einem Startmenü-Eintrag für alle
Benutzer des Geräts. Zum Benutzen brauchst du keine eigenen Rechte, und von deinem Konto aus lässt
sich an der App nichts verändern.

Beim ersten Start für dich schaltet die App **Start with Windows** ein. Ausschalten kannst du das
unter **Settings → Behavior** oder im Tray-Menü; die App schaltet es danach nie wieder ein.

Die App hat kein eigenes Fenster in der Taskleiste. Sie lebt im Infobereich — den Symbolen neben der
Uhr. Windows versteckt neue Symbole dort gern: Wenn du es nicht siehst, klick auf den Pfeil **^**
und zieh das Symbol auf den sichtbaren Teil der Leiste, damit es dort bleibt.

## Konto verbinden

Klick auf das Symbol. Beim ersten Mal steht dort **Welcome to ModernWork Elevate**.

Wie es weitergeht, hängt davon ab, wie deine IT die App eingerichtet hat:

- **Tenants per Richtlinie vorkonfiguriert** — der Tenant ist schon hinterlegt. **Add account**,
  dann **Sign in**.
- **Nichts vorkonfiguriert** — **Set up your first tenant**: Tenant-ID und Client-ID der App
  Registration deiner Organisation eintragen. Beides bekommst du von deinem Admin — raten kann man
  sie nicht. Einen Tenant, der schon da ist, lehnt das Formular ab: „This tenant is already
  configured. Use the gear on its card to change it." Danach das Konto hinzufügen.

Hat deine IT die Tenant-Liste gesperrt, kannst du selbst keine Tenants hinzufügen oder ändern; die
App sagt es dann.

**Sign in** öffnet die normale Windows-Kontoauswahl. Nimm das Admin-Konto, mit dem du die Rollen
hältst — bei vielen ist das ein anderes als das, mit dem sie sich an Windows anmelden. In der App
gibt es keine Passwortabfrage: Windows erledigt die Anmeldung (mit Windows Hello, einem Passkey oder
der Anmeldeseite deiner Organisation), die App sieht dein Passwort nie.

Du kannst mehrere Konten in mehreren Tenants verbinden. Umschalten musst du zwischen ihnen nicht —
alles steht in einer Liste, nach Tenant gruppiert.

## Das Tray-Symbol lesen

Das Symbol beantwortet „habe ich gerade Rechte?", ohne dass du etwas öffnest. Mauszeiger drauf für
die Details.

| Symbol | Bedeutung |
| --- | --- |
| **Rot** | Nicht angemeldet — kein Konto verbunden, oder die Sitzung ist abgelaufen |
| **Grau** | Angemeldet, nichts aktiv. Der Normalzustand |
| **Grün** | Mindestens eine Rolle ist aktiv. Der Tooltip zählt sie |
| **Gelb** | Eine aktive Rolle läuft gleich ab. Der Tooltip nennt sie und zählt herunter |

Die blaue Pfeil-Kachel ist für helle wie dunkle Taskleisten gezeichnet und folgt ihr, wenn du das
Windows-Design umstellst. Bedeutung trägt allein der kleine Punkt.

## Das Fenster

Ein Klick auf das Symbol öffnet das Fenster. Von oben nach unten:

**ACTIVE** — was gerade eingeschaltet ist, jeweils mit Countdown-Balken; erscheint nur, solange
etwas aktiv ist. Wegen dieses Abschnitts lohnt der Blick in die App auch dann, wenn du nichts
aktivierst.

**PINNED** — Rollen und gespeicherte Scope-Sets, die du mit einem Stern markiert hast. Deine
Sortierung, bleibt über Neustarts erhalten.

**RECENT** — die letzten drei aktivierten Rollen. Füllt sich von selbst, nichts zu pflegen.

**ELIGIBILITIES** — alles, was du aktivieren darfst, nach Tenant gruppiert. Gruppen lassen sich
zuklappen, und die App merkt sich, welche du zugelassen hast.

Das **Suchfeld** durchsucht Rollenname, Rollentyp, Tenant *und* Azure-Scope gleichzeitig — der Name
einer Subscription findet also die Rollen, die für sie gelten. **⟳** neben ELIGIBILITIES erzwingt
eine Aktualisierung, nötig ist das aber selten: Die Liste hält sich selbst aktuell, auch bei
Aktivierungen, Abläufen und Rollen, die anderswo beendet wurden. **⚙** unten rechts öffnet die
Einstellungen.

Ganz unten steht die Version, die du benutzt — gib sie an, wenn du ein Problem meldest. Ein Klick
darauf öffnet ihre Release Notes; **mistermodernwork** und das GitHub-Symbol daneben öffnen das
Profil des Autors und die Projektseite. Die App öffnet diese Seiten nur auf Klick; sie verbindet
sich nie selbst mit GitHub.

## Eine Rolle aktivieren

Klick auf eine Rolle. Das Aktivierungsformular schiebt sich ein.

**Duration** — ein Schieberegler in Halbstundenschritten. Sein Maximum entscheidet nicht die App,
sondern die Richtlinie, die dein Admin für diese Rolle gesetzt hat; verschiedene Rollen haben
verschiedene Maxima. Den Startwert legst du unter **Settings → Behavior → Default activation
duration** fest.

**Justification** — warum du sie brauchst. Das landet im Audit-Log deines Tenants und wird von
echten Menschen gelesen; „Arbeit" hilft später niemandem. Wenn du immer denselben Satz tippst,
speichere ihn mit **+ Save current** unter **FAVOURITES**: Er kommt für diese Rolle als
Ein-Klick-Favorit zurück.

**Ticket number / Ticket system** — wird nur gefragt, wenn die Richtlinie der Rolle es verlangt. Das
Ticketsystem ist pro Tenant vorbelegt, falls dein Admin eines hinterlegt hat; die Nummer tippst du.

Dann **Activate**. Die Rolle wandert innerhalb weniger Sekunden nach ACTIVE.

### Hinweise, die man lesen sollte

**ℹ This activation requires approval** — du bekommst die Rolle jetzt nicht. Die Anfrage geht an
einen Genehmiger und wartet. Die Zeile bleibt als ausstehend sichtbar, und die Rolle wird aktiv,
sobald zugestimmt wurde. Von deiner Seite ist nichts weiter zu tun — dränge den Genehmiger, nicht
die App.

**ℹ This activation may require additional verification (MFA)** — Windows verlangt während der
Aktivierung eine Bestätigung.

**⚠ This role activates without asking you to sign in again** — die Einstellungen der Rolle in deinem
Tenant verlangen keine erneute Anmeldung; nichts lässt dich beweisen, dass du es bist, bevor die
Rolle aktiv wird. **How to set it up** öffnet die Anleitung für deinen PIM-Admin
([docs/fresh-sign-in.md](fresh-sign-in.md), auf Englisch). Der Hinweis bleibt, bis die Rolle einen
Authentication Context verlangt. Bei den meisten Azure-Ressourcenrollen kann die App die
Einstellungen der Rolle nicht lesen und zeigt keinen der beiden Hinweise.

**⚠ This group can carry directory roles** — bei einem PIM-for-Groups-Eintrag. Eine
Gruppenmitgliedschaft kann Admin-Rollen mitbringen; die Aktivierung gibt dir also womöglich mehr,
als der Gruppenname vermuten lässt. Gut zu wissen, was man da einschaltet.

## Azure-Rollen: wo die Rolle gelten soll

Eine Azure-Rolle auf einer Management Group gilt für alles darunter — das können sämtliche
Subscriptions eures Unternehmens sein. Das alles zu aktivieren, um in einer zu arbeiten, ist
unnötige Angriffsfläche. Deshalb fragt die App nach.

Solche Rollen haben einen Knopf **Activate on**. Er öffnet eine Liste: oben ein Suchfeld, dann die
**Subscriptions**, dann die **Management Groups**, zuletzt **Entire scope — everything listed
above**. Die Reihenfolge ist Absicht: kleinste Wirkung zuerst.

**Nichts ist vorausgewählt — auch nicht der Scope, auf dem deine Berechtigung liegt.** Activate
bleibt gesperrt, bis du geantwortet hast. Alles zu nehmen steht am Ende der Liste zur Verfügung,
aber als Entscheidung — nicht als das, was passiert, wenn man das Formular in Ruhe lässt.

Jeder angehakte Scope wird eine eigene Aktivierung. Hakst du drei Subscriptions an, zeigt ACTIVE
drei Zeilen, jede mit eigenem Countdown, jede einzeln beendbar.

Resource Groups werden nicht angeboten, einzelne Ressourcen auch nicht. Im Portal gibt es sie, aber
die Liste wäre endlos — das versucht die App gar nicht erst.

### Scopes als Set speichern

Wochenlang in denselben zwei Subscriptions zu arbeiten ist normal. Sie täglich neu anzuhaken nicht.

Hak an, was du brauchst, gib dem Ganzen einen Namen („Projekt Contoso" — optional) und speichere es.
Es erscheint unter **FAVOURITE SCOPES** im Formular dieser Rolle. Ein Klick hakt genau diese Scopes
an; klickst du ein anderes Set an, **ersetzt** es die Auswahl, statt sie zu ergänzen — so können
sich Sets nicht unbemerkt aufsummieren.

Markierst du ein Set mit dem Stern, steht es zusätzlich unter **PINNED** auf der Hauptseite, neben
deinen angehefteten Rollen — ein Klick von der Startseite zum ausgefüllten Formular. Eine Rolle
anzuheften und ein Scope-Set anzuheften ist bewusst dieselbe Geste.

Gespeicherte Sets liegen auf deinem Rechner (`scope-favorites.json`), nie im Tenant. Niemand sonst
sieht sie.

## Eine Rolle vorzeitig beenden

Jede ACTIVE-Zeile hat einen **Stop**-Knopf. Benutz ihn, wenn du fertig bist — der Sinn von PIM ist
ja gerade, dass Rechte nicht herumliegen.

**In den ersten fünf Minuten ist der Knopf grau und tut nichts.** Das ist kein Fehler und keine
Vorsicht der App: Microsoft lehnt es ab, eine Rolle zu deaktivieren, die noch keine fünf Minuten
aktiv ist. Fahr mit der Maus drüber, dann steht dort, wie lange noch. Danach wird er rot und
funktioniert.

Eine Rolle nicht zu beenden kostet nichts — sie läuft von selbst ab.

## Kurz vor Ablauf

Kurz bevor eine aktive Rolle ausläuft, meldet sich eine Benachrichtigung, und das Tray-Symbol wird
gelb. Der Countdown-Balken der Zeile wechselt gegen Ende die Farbe.

Ob gewarnt wird und wie früh, steht unter **Settings → Notifications**.

## Sich mehr Zeit verschaffen

PIM kann eine laufende Aktivierung nicht verlängern. Das hat diese App nicht vergessen — Microsofts
API lehnt eine zweite Aktivierung einer Rolle ab, die du noch hast, ganz gleich welches Ende du
verlangst, und im Entra-Portal gibt es dafür ebenso wenig einen Knopf.

Der Knopf **↻ Extend time** tut deshalb das Einzige, was dir mehr Zeit verschafft: Er beendet die
laufende Aktivierung und beantragt sofort eine neue über die volle Dauer. Du findest ihn neben dem
Stop-Knopf in jeder ACTIVE-Zeile und in der Ablaufwarnung.

Bevor du ihn drückst:

- **Du verlierst die Rolle kurz.** Die App wartet, bis Microsoft sie wirklich losgelassen hat —
  meist ein paar Sekunden, im schlechten Fall ein paar Minuten — und fragt erst dann erneut danach.
  Drück ihn nicht mitten in etwas, das kaputtgeht, wenn deine Rechte kurz blinken.
- **Es ist vorausgefüllt.** Bei einer Azure-Rolle ist die Subscription bzw. Management Group, auf
  der die ablaufende Aktivierung läuft, bereits angehakt, und als Begründung steht die vom letzten
  Mal mit angehängtem `- extend time`. Beides kannst du vor dem Absenden ändern. Die Begründung
  merkt sich die App nur zur Laufzeit — nach einem Neustart oder bei einer anderswo aktivierten
  Rolle bleibt das Feld leer.
- **Es ist eine neue Aktivierung.** Eigene Begründung, eigener Eintrag im Audit-Log, genau so, als
  hättest du bei der Eligibility angefangen.
- **Die ersten fünf Minuten sind gesperrt**, aus demselben Grund wie beim Stop-Knopf.
- **Rollen mit Genehmigung bieten ihn nicht an.** Die laufende Aktivierung zu beenden würde dich
  ohne jeden Zugriff auf einen Genehmiger warten lassen. Dort bleibt nur: ablaufen lassen und neu
  beantragen — sag deinem Genehmiger also vorher Bescheid.

## Einstellungen

Das Zahnrad unten rechts.

**APPEARANCE** — Theme: System, hell oder dunkel. **Logo** setzt das Logo deiner Organisation oben
rechts ins Fenster: **Choose…** nimmt ein PNG, **Remove** entfernt es wieder.

**BEHAVIOR** — Mit Windows starten. Standard-Aktivierungsdauer (der
Startwert des Schiebereglers, keine Obergrenze — die kommt aus der Richtlinie deines Admins).

**NOTIFICATIONS** — vor Ablauf benachrichtigen, und wie früh.


**DIAGNOSTICS** — Log-Detailgrad, Log-Ordner öffnen, und ein **Network check**, der jeden Endpunkt
prüft, den die App und die Windows-Anmeldung brauchen. **Copy report** legt das Ergebnis in einer
Form in die Zwischenablage, die du in ein Ticket einfügen kannst. Der richtige Griff, wenn die
Anmeldung in einem abgeschotteten Netz scheitert.

**TENANTS** — eine Karte pro Tenant: dessen App Registration, dessen Ticketsystem und die dort
angemeldeten Konten. Du kannst einem Konto ein kurzes Kürzel geben („EADM"), statt überall einen
langen UPN zu lesen, und die Tenants in deine Reihenfolge ziehen. Das Zahnrad einer Karte zeigt
ihre Tenant-ID, die du markieren und kopieren kannst. **Remove tenant** unter dem Zahnrad entfernt
den Tenant samt seinen Konten und deren Anmeldungen; ein einzelnes Konto entfernst du unter seinem
Stift. Beides fragt mit einem zweiten Klick nach.

Über das Zahnrad einer Karte änderst du auch die Client-ID des Tenants. Hat der Tenant Konten, wird Save zuerst zu „Remove N accounts and save" und fragt mit einem zweiten Klick
nach: Die Konten, die über die alte App Registration angemeldet sind, werden entfernt, und du fügst
sie nach dem Neustart neu hinzu. Eine geänderte App Registration greift erst nach einem Neustart —
die App sagt es dann auch; bis dahin bleibt **+ Add account…** auf dieser Karte gesperrt. Tenants
aus der Richtlinie deiner Organisation lassen sich hier nicht ändern.

## Updates

Die App aktualisiert sich nicht selbst und verbindet sich nie mit GitHub. Neue Versionen kommen
über die Softwareverteilung deiner Organisation; Einstellungen, Konten und Favoriten bleiben bei
jedem Update erhalten.

## Wenn etwas nicht funktioniert

**Keine Rollen in der Liste, obwohl du welche hast.** Prüf das Konto oben im Fenster — man ist
schnell mit dem Alltagskonto statt mit dem Admin-Konto angemeldet. Stimmt das Konto, ist die
Berechtigung tatsächlich nicht da, und das ist eine Frage an die Administration des Tenants.

**Die Azure-Rollen fehlen, die anderen sind da.** Im Fenster steht eine Zeile, die mit *Azure
resource roles unavailable* beginnt — was danach kommt, ist die Ursache. Ein fehlender Consent
erledigt sich von allein, sobald ein Admin ihn erteilt. Eine Conditional-Access-Richtlinie, die ein
verwaltetes Gerät verlangt, gehört zur IT.

Die häufigsten Ursachen:

- *could not be reached* — ein Proxy oder eine Firewall blockiert Azure Resource Manager.
  **Settings → Diagnostics → Network check** ausführen und den Bericht ans Netzwerkteam schicken.
- *the proxy asks for authentication* — der Proxy verlangt für Azure Resource Manager eine eigene
  Anmeldung; das Netzwerkteam gibt den Endpunkt für das Gerät frei.
- *did not get through this time* — Netzwerk oder Windows-Anmeldung waren noch nicht bereit, meist
  direkt nach der Anmeldung am PC. Die App lädt von selbst neu, sobald das Netzwerk wieder da ist,
  und alle 20 Sekunden, solange der Hinweis steht.
- *background sign-in for Azure was refused* oder ein Consent-Text — ein Admin erteilt die
  Zustimmung für Azure Service Management, siehe unten.

**Refresh** fragt Azure sofort erneut ab.

**Ein Tenant meldet „Sign-in required".** Die Sitzung dieses Kontos ist abgelaufen, MFA ist wieder
fällig oder das Passwort wurde geändert. Die App öffnet nie von selbst ein Anmeldefenster; klick auf
**Refresh** (↻) und melde dich im Fenster an, das sich öffnet – eines pro betroffenem Konto.

**Das Anmeldefenster öffnet sich und bleibt leer.** Meist blockiert das Netzwerk einen
Microsoft-Endpunkt. **Settings → Diagnostics → Network check** ausführen, **Copy report** drücken
und das an die IT schicken — darin steht, welcher Endpunkt blockiert ist, statt dass jemand raten
muss.

**Ein Tenant zeigt „PIM is not available in this tenant" oder „PIM is not set up in this tenant".**
Dem Tenant fehlt die Lizenz Microsoft Entra ID P2 oder Governance, die PIM voraussetzt. Bis er eine
hat, gibt es dort nichts zu aktivieren.

**Die Anmeldung scheitert mit einer Consent- oder Berechtigungsmeldung.** Der App Registration wurde
in diesem Tenant nicht zugestimmt. Ein Admin macht das einmalig, siehe
[app-registration-setup.md](app-registration-setup.md). Bleiben die Azure-Ressourcenrollen danach
unerreichbar, erteilt ein Admin die Zustimmung unter Enterprise applications → (deine App) →
Permissions mit „Grant admin consent".

**Die Aktivierung scheitert und nennt Authentication Context oder Conditional Access.** Die Rolle
verlangt eine stärkere Anmeldung, als die Sitzung hat. Erneut aktivieren und die Windows-Anmeldung
abschliessen; scheitert es weiter, erfüllt das Gerät die Richtlinie nicht — das ist ein Fall für
die IT.

**Die Aktivierung braucht eine Genehmigung und es passiert nichts.** Sie wartet auf einen Menschen.
Die App kann das nicht beschleunigen.

**Der Stop-Knopf ist grau.** Die ersten fünf Minuten, siehe oben.

**Das Tray-Symbol ist weg.** Windows hat es versteckt. Auf den Pfeil **^** neben der Uhr klicken und
zurück auf die Leiste ziehen.

**Eine Benachrichtigung meldet „Unexpected error".** Etwas ist fehlgeschlagen, womit die App nicht
gerechnet hat. Sie läuft weiter; was du gerade angeklickt hast, hat womöglich nicht gegriffen, also
prüfen und noch einmal versuchen. Die Ursache steht im Log.

**Alles andere** — **Settings → Diagnostics → Open log folder**. Das Log enthält keine Passwörter,
keine Tokens und keinen Begründungstext; es kann bedenkenlos an ein Ticket angehängt werden.

## Was auf deinem Rechner landet

In deinem eigenen Profil, lesbar nur für dich (und das System):

- `%APPDATA%\ModernWorkElevate` — deine Einstellungen, angehefteten Rollen, gespeicherten
  Begründungen und Scope-Sets, die verbundenen Konten, selbst hinzugefügte App-Registration-Einträge
  und das Logo deiner Organisation, falls es eines gibt
- `%LOCALAPPDATA%\ModernWorkElevate\logs` — die Logdateien, sieben Tage aufbewahrt
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` — der Autostart-Wert, solange Autostart an ist

**Weder Passwörter noch Tokens werden je auf den Datenträger geschrieben.** Der Token-Broker von
Windows hält deine Anmeldungen; Zugriffstokens liegen nur im Arbeitsspeicher, solange die App läuft.

Um deine Daten zu entfernen und die Konten der App beim Token-Broker abzumelden, führt die IT
`Remove-ModernWorkElevateUserData.ps1` aus (siehe [deployment.md](deployment.md)). Ein Konto, das du
Windows selbst hinzugefügt hast (Einstellungen → Konten → Auf Arbeits- oder Schulkonto zugreifen),
bleibt dort.
