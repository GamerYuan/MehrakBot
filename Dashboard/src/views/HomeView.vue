<script setup>
import { useRouter } from "vue-router";
const router = useRouter();
</script>

<template>
  <div class="container">
    <div class="top-nav">
      <button @click="router.push('/login')" class="btn dashboard-btn">
        Dashboard
      </button>
    </div>

    <header>
      <h1>MehrakBot</h1>
      <p class="tagline">Your All-in-One HoYoverse games Toolbox</p>
    </header>

    <main>
      <div class="actions">
        <a
          href="https://discord.com/oauth2/authorize?client_id=1365154828430610532"
          class="btn primary"
          target="_blank"
          rel="noopener noreferrer"
          >Add to Discord</a
        >
        <a
          href="https://gameryuan.gitbook.io/mehrak/"
          class="btn secondary"
          target="_blank"
          rel="noopener noreferrer"
          >Documentation</a
        >
      </div>

      <div class="features">
        <div class="card">
          <h2>Common Utilities</h2>
          <p>Multi-profile support, HoYoLAB Daily Check In</p>
        </div>
        <div class="card">
          <h2>Genshin Impact</h2>
          <p>
            Character build card, Abyss summary card, Code redemption, and more.
          </p>
        </div>
        <div class="card">
          <h2>Honkai: Star Rail</h2>
          <p>
            Character build card, Memory of Chaos summary card, Code redemption,
            and more.
          </p>
        </div>
        <div class="card">
          <h2>Zenless Zone Zero</h2>
          <p>
            Character build card, Shiyu Defense summary card, Code redemption,
            and more.
          </p>
        </div>
        <div class="card">
          <h2>Honkai Impact: 3rd</h2>
          <p>Character build card, and more coming soon.</p>
        </div>
      </div>
    </main>

    <footer>
      <p>&copy; 2026 MehrakBot. Not affiliated with HoYoverse.</p>
    </footer>
  </div>
</template>

<style scoped>
.container {
  display: flex;
  flex-direction: column;
  gap: 4rem;
  position: relative;
}

.top-nav {
  position: absolute;
  top: 0;
  right: 0;
}

.dashboard-btn {
  background-color: transparent;
  border: 1px solid var(--primary-color);
  color: var(--primary-color);
  font-size: 0.9rem;
  padding: 0.5rem 1rem;
}

.dashboard-btn:hover {
  background-color: var(--primary-color);
  color: white;
}

.tagline {
  font-size: 1.5rem;
  color: #aaa;
}

.actions {
  display: flex;
  gap: 1rem;
  justify-content: center;
  margin-bottom: 3rem;
}

.btn {
  padding: 0.8rem 1.5rem;
  border-radius: 8px;
  font-weight: bold;
  transition: transform 0.2s, background-color 0.2s, color 0.2s;
  text-decoration: none;
  display: inline-block;
}

.btn:hover {
  transform: translateY(-2px);
}

.btn.primary {
  background-color: var(--primary-color);
  color: white;
}

.btn.secondary {
  background-color: var(--card-bg);
  border: 1px solid #555;
  color: white;
}

.features {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 2rem;
  max-width: 1200px;
  margin: 0 auto;
}

.card {
  background-color: var(--card-bg);
  padding: 2rem;
  border-radius: 12px;
  text-align: left;
  border: 1px solid #333;
  transition: transform 0.3s ease, border-color 0.3s ease, box-shadow 0.3s ease;
  display: flex;
  flex-direction: column;
  flex: 1 1 300px;
  max-width: 380px;
  min-height: 200px;
}

.card:hover {
  transform: translateY(-5px);
  border-color: var(--primary-color);
  box-shadow: 0 10px 20px rgba(0, 0, 0, 0.2);
}

.card h2 {
  color: var(--secondary-color);
  margin-top: 0;
  margin-bottom: 1rem;
}

footer {
  color: #666;
  font-size: 0.9rem;
}
</style>
