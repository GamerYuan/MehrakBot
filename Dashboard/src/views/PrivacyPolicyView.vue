<script setup>
import AppNavbar from "../components/AppNavbar.vue";
</script>

<template>
  <div class="privacy-page">
    <AppNavbar />

    <main class="privacy-content">
      <div class="container">
        <h1>Privacy Policy</h1>

        <h2>Privacy Policy for Mehrak Discord Bot</h2>
        <p class="last-updated"><strong>Last Updated:</strong> June 10, 2025</p>

        <h3>1. Introduction</h3>
        <p>
          This Privacy Policy describes how Mehrak ("the Bot", "we", "us", or
          "our") collects, uses, and protects your information when you use our
          Discord bot services. Mehrak is an all-in-one toolbox for HoYoverse
          games, developed and operated by GamerYuan.
        </p>
        <p>
          By using Mehrak, you agree to the collection and use of information in
          accordance with this Privacy Policy.
        </p>

        <h3>2. Information We Collect</h3>

        <h4>2.1 Automatically Collected Information</h4>
        <p>
          When you interact with Mehrak, we automatically collect the following
          information:
        </p>
        <ul class="list-disc">
          <li>
            <strong>Discord User ID:</strong> Your unique Discord identifier for
            profile identification
          </li>
          <li>
            <strong>Usage Statistics:</strong> Data about how you interact with
            the bot for metrics and analysis
          </li>
          <li>
            <strong>HoYoverse Game UIDs:</strong> Your game profile identifiers
            for HoYoverse games
          </li>
          <li>
            <strong>HoYoverse Cookies:</strong> Authentication tokens to access
            HoYoverse public APIs on your behalf
          </li>
          <li>
            <strong>Passphrase:</strong> A user-provided passphrase used for
            encrypting your HoYoverse cookies
          </li>
        </ul>

        <h4>2.2 How We Collect Information</h4>
        <p>
          All information is collected automatically when you interact with the
          bot through Discord commands and features.
        </p>

        <h3>3. How We Use Your Information</h3>
        <p>We use the collected information for the following purposes:</p>
        <ul class="list-disc">
          <li>
            <strong>Discord User ID:</strong> To identify and associate your
            Discord profile with your bot data
          </li>
          <li>
            <strong>Usage Statistics:</strong> To analyze bot performance,
            improve features, and understand user behavior
          </li>
          <li>
            <strong>HoYoverse Game UIDs:</strong> To identify and retrieve
            information from your HoYoverse game profiles
          </li>
          <li>
            <strong>HoYoverse Cookies:</strong> To authenticate and access
            HoYoverse public APIs to query your in-game details
          </li>
          <li>
            <strong>Passphrase:</strong> To generate encryption keys for
            securing your HoYoverse cookies
          </li>
        </ul>

        <h3>4. Data Storage and Security</h3>

        <h4>4.1 Storage Location</h4>
        <p>
          Your data is stored securely on our Hetzner VPS infrastructure located
          in Germany.
        </p>

        <h4>4.2 Data Retention</h4>
        <p>
          We retain your data for as long as you maintain a profile with the
          bot. You can delete your profile and all associated data at any time
          using the bot's delete profile functionality.
        </p>

        <h4>4.3 Security Measures</h4>
        <p>We implement robust security measures to protect your data:</p>
        <ul class="list-disc">
          <li>
            <strong>Zero-Knowledge Encryption:</strong> Your HoYoverse cookies
            are encrypted using AES-256 encryption
          </li>
          <li>
            <strong>User-Controlled Encryption:</strong> Your provided
            passphrase generates the encryption key, ensuring we cannot access
            your cookies without your passphrase
          </li>
          <li>
            <strong>Secure Storage:</strong> All data is stored on secure
            servers with appropriate access controls
          </li>
        </ul>

        <h3>5. Data Sharing</h3>
        <p>
          We do not share, sell, trade, or transfer your personal information to
          third parties. Your data is used exclusively for providing bot
          services and is not shared with any external organizations or
          services.
        </p>

        <h3>6. Third-Party Services</h3>
        <p>
          Mehrak accesses HoYoverse public APIs using your provided cookies to
          retrieve your in-game information. This interaction is necessary for
          the bot's functionality and is performed securely using your encrypted
          authentication data.
        </p>

        <h3>7. Your Rights and Choices</h3>

        <h4>7.1 Data Deletion</h4>
        <p>
          You have the right to delete your profile and all associated data at
          any time using the bot's built-in delete profile feature. Once
          deleted, all your data will be permanently removed from our systems.
        </p>

        <h4>7.2 Data Access</h4>
        <p>
          You can view what data we have stored about you through the bot's
          profile management commands.
        </p>

        <h4>7.3 Data Portability</h4>
        <p>
          You can request a copy of your data by contacting us through the
          methods listed below.
        </p>

        <h3>8. Bot Permissions</h3>
        <p>
          Mehrak operates with minimal Discord permissions and does not require
          any special administrative permissions. The bot only accesses
          information necessary for its functionality.
        </p>

        <h3>9. Changes to This Privacy Policy</h3>
        <p>
          We may update this Privacy Policy from time to time. We will notify
          users of any material changes by updating the "Last Updated" date at
          the top of this Privacy Policy. Continued use of the bot after such
          changes constitutes acceptance of the updated Privacy Policy.
        </p>

        <h3>10. Contact Information</h3>
        <p>
          If you have any questions, concerns, or requests regarding this
          Privacy Policy or your data, please contact us:
        </p>
        <ul class="list-disc">
          <li>
            <strong>Discord:</strong> Contact GamerYuan directly on Discord
          </li>
          <li>
            <strong>Email: </strong>
            <a href="mailto:f2pplayer0801@gmail.com">f2pplayer0801@gmail.com</a>
          </li>
        </ul>

        <h3>11. Compliance</h3>
        <p>
          This Privacy Policy is designed to comply with applicable data
          protection laws, including but not limited to the General Data
          Protection Regulation (GDPR) and other relevant privacy regulations.
        </p>

        <h3>12. Children's Privacy</h3>
        <p>
          Mehrak is not intended for users under the age of 13. We do not
          knowingly collect personal information from children under 13. If you
          become aware that a child has provided us with personal information,
          please contact us immediately.
        </p>
      </div>
    </main>
  </div>
</template>

<style scoped>
.privacy-page {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  background: linear-gradient(to bottom, #0a0a0f, #111118);
}

.privacy-content {
  flex: 1;
  padding: 8rem 2rem 4rem;
}

.container {
  max-width: 800px;
  margin: 0 auto;
}

h1 {
  font-size: 2.5rem;
  font-weight: 700;
  color: #fff;
  margin: 0 0 2rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
}

h2 {
  font-size: 1.5rem;
  font-weight: 600;
  color: #fff;
  margin: 2rem 0 1rem;
}

h3 {
  font-size: 1.2rem;
  font-weight: 600;
  color: #e0e0e0;
  margin: 2rem 0 1rem;
}

h4 {
  font-size: 1rem;
  font-weight: 600;
  color: #c0c0c0;
  margin: 1.5rem 0 0.75rem;
}

p {
  color: #a0a0a0;
  line-height: 1.7;
  margin: 0 0 1rem;
}

.last-updated {
  color: #808080;
  margin-bottom: 2rem;
}

ul {
  color: #a0a0a0;
  line-height: 1.7;
  margin: 0 0 1rem;
  padding-left: 1.5rem;
}

li {
  margin-bottom: 0.5rem;
}

a {
  color: var(--accent);
  text-decoration: none;
}

a:hover {
  text-decoration: underline;
}

@media (max-width: 640px) {
  .privacy-content {
    padding: 6rem 1rem 2rem;
  }

  h1 {
    font-size: 1.8rem;
  }

  h2 {
    font-size: 1.3rem;
  }

  h3 {
    font-size: 1.1rem;
  }
}
</style>
