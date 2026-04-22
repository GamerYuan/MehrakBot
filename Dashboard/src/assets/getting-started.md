# Getting Started

You can use Mehrak by inviting Mehrak to your Discord server, or installing Mehrak to your Discord user profile

## Inviting to Discord Server

{% hint style="info" %}
You are required to have `Manage Server` or `Administrator` permissions in a server to invite Discord bots into said server
{% endhint %}

{% stepper %}
{% step %}

### Go to Mehrak Install Page

Click on this link to go to the [Mehrak install page](https://discord.com/oauth2/authorize?client_id=1365154828430610532) and select **Add to Server**
{% endstep %}

{% step %}

### Select your server

Select the server you want Mehrak to be installed in
{% endstep %}

{% step %}

### Authorise required permissions

Mehrak does not require any special permissions. You are only required to give it the `Send Messages` permission
{% endstep %}

{% step %}

### Done!

You have successfully installed Mehrak to that server
{% endstep %}
{% endstepper %}

## Installing to Discord User Profile

{% stepper %}
{% step %}

### Go to Mehrak Install Page

Click on this link to go to [Mehrak install page](https://discord.com/oauth2/authorize?client_id=1365154828430610532) and select **Install to account**
{% endstep %}

{% step %}

### Authorise required permissions

Mehrak does not require any special permissions
{% endstep %}

{% step %}

### Done!

You have successfully installed Mehrak to your user account. You may now use Mehrak in any Discord server with External Applications enabled, or in DMs
{% endstep %}
{% endstepper %}

## Using Commands

Mehrak utilises Discord's Slash Command, with a successful installation, you can access the list of Mehrak commands by typing `/` in supported text channels

### Adding a profile

To use game related commands, you must first add a profile to Mehrak. First run the following command

```sh
/profile add
```

Upon running the Profile Add command, you will be prompted with an authentication modal, with the following input fields

- HoYoLAB UID
- HoYoLAB Cookies
- Passphrase

{% hint style="info" %}
You are advised to use a computer to complete the authentication, as these steps are more easily executed with a computer
{% endhint %}

You may find these information with the following steps:

#### HoYoLAB UID

{% stepper %}
{% step %}

### Login to HoYoLAB

Proceed to HoYoLAB and login with your HoYoverse Account
{% endstep %}

{% step %}

#### Go to your profile page

Click on your profile picture on the top right corner, and select **Personal Homepage**
{% endstep %}

{% step %}

#### Copy your HoYoLAB UID from the URL bar

You can now copy your HoYoLAB UID from the URL bar. It is denoted as the sequence of numbers after `id=`&#x20;

<figure><img src="https://247740754-files.gitbook.io/~/files/v0/b/gitbook-x-prod.appspot.com/o/spaces%2FHAezmyesx9uvB1IBn9YB%2Fuploads%2FTcsJMH7nIo6WXCjxo2C0%2Fimage.png?alt=media&#x26;token=3b97abc5-b666-46be-9967-89ee0c68d140" alt=""><figcaption><p>In this example, the HoYoLAB UID will be 36639475</p></figcaption></figure>
{% endstep %}
{% endstepper %}

#### HoYoLAB Cookies

Mehrak requires your HoYoLAB Cookies to obtain your in-game information to perform other commands. You are highly advised to read the [Security](https://gameryuan.gitbook.io/mehrak/resources/security) page on the security concerns of providing this data to Mehrak

{% stepper %}
{% step %}

#### Login to HoYoLAB

Proceed to HoYoLAB and login with your HoYoverse Account. This should be the same account you have logged in to obtain your HoYoLAB UID
{% endstep %}

{% step %}

#### Open browser Developer Tools

Press `Ctrl` + `Shift` + `I` to open your browser's Developer Tools
{% endstep %}

{% step %}

#### Obtain HoYoLAB Cookies

You may now obtain your HoYoLAB Cookies. Different browsers may put them in different locations, but you can follow this general guideline

- Chromium-Based Browsers (Chrome, MS Edge, Opera, Brave etc.)
  - Go to the Application Tab in your Developer Tool. Under Storage, select Cookies and click on the website. Scroll down on the right panel. Copy the value associated with the `ltoken_v2` entry

<figure><img src="https://247740754-files.gitbook.io/~/files/v0/b/gitbook-x-prod.appspot.com/o/spaces%2FHAezmyesx9uvB1IBn9YB%2Fuploads%2FiJfzDARWHm3nqRngFMbm%2FCookie.png?alt=media&#x26;token=2c99c648-765b-47a7-a212-8582964ff76b" alt=""><figcaption><p>Developer Console for Chromium-Based Browser</p></figcaption></figure>

- Firefox-Based Browsers (Firefox, LibreWolf etc.)
  - Go to the Storage Tab in your Developer Tool. Select Cookies and click on the website. Scroll down on the right panel. Copy the value associated with the `ltoken_v2` entry

<figure><img src="https://247740754-files.gitbook.io/~/files/v0/b/gitbook-x-prod.appspot.com/o/spaces%2FHAezmyesx9uvB1IBn9YB%2Fuploads%2FDBtUUPx1m4xPKlITo8X3%2FCookie%20Firefox.png?alt=media&#x26;token=b79b263b-929a-44f7-9142-10b8ceed7e30" alt=""><figcaption><p>Developer Console for Firefox-Based Browser</p></figcaption></figure>
{% endstep %}

{% step %}

#### Provide HoYoLAB Cookies

With the cookie value obtained from your browser, paste that into the text input field. Your cookie should begin with `v2_C...`&#x20;
{% endstep %}
{% endstepper %}

#### Passphrase

To ensure the security of your stored cookies, Mehrak requires all users to provide a passphrase, which is used to securely encrypt your cookie on our database. Unlike a password, a passphrase is designed to be easily remembered by humans, but difficult to crack by machines

You should follow these practices when setting a passphrase

- Choose a sentence that you would remember, this could be a quote from a movie, a book, or your favourite game character
- The chosen sentence should be sufficiently long, at least 20 characters long to ensure good security
- The chosen sentence has not been used as passphrase for other services, even if it were for other profiles of Mehrak

The chosen passphrase will be used verbatim for the encryption, this includes any leading or trailing white spaces, paragraphs and line breaks

Mehrak does not store any passphrases on the database. Your passphrase is required to execute any commands that requires the use of your HoYoLAB Cookies. For your convenience purposes, after authenticating for any command execution, your decrypted cookie will be cached for 5 mins before being actively evicted from the cache, during which you can execute additional commands without the need of typing in your passphrase
