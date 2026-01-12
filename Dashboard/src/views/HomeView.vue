<script setup>
import { useRouter } from "vue-router";
import Button from "primevue/button";
import Card from "primevue/card";

const router = useRouter();
</script>

<template>
  <div class="container">
    <div class="top-nav">
      <Button label="Dashboard" outlined @click="router.push('/dashboard')" />
    </div>

    <header>
      <h1>MehrakBot</h1>
      <p class="tagline">Your All-in-One HoYoverse games Toolbox</p>
    </header>

    <main>
      <div class="actions">
        <Button
          as="a"
          label="Add to Discord"
          href="https://discord.com/oauth2/authorize?client_id=1365154828430610532"
          target="_blank"
          rel="noopener noreferrer"
        />
        <Button
          as="a"
          label="Documentation"
          href="https://gameryuan.gitbook.io/mehrak/"
          target="_blank"
          rel="noopener noreferrer"
          severity="secondary"
          outlined
        />
      </div>

      <div class="features">
        <Card class="feature-card bg-zinc-800! border! border-zinc-700!">
          <template #title>Common Utilities</template>
          <template #content>
            <p>Multi-profile support, HoYoLAB Daily Check In</p>
          </template>
        </Card>
        <Card class="feature-card bg-zinc-800! border! border-zinc-700!">
          <template #title>Genshin Impact</template>
          <template #content>
            <p>
              Character build card, Abyss summary card, Code redemption, and
              more.
            </p>
          </template>
        </Card>
        <Card class="feature-card bg-zinc-800! border! border-zinc-700!">
          <template #title>Honkai: Star Rail</template>
          <template #content>
            <p>
              Character build card, Memory of Chaos summary card, Code
              redemption, and more.
            </p>
          </template>
        </Card>
        <Card class="feature-card bg-zinc-800! border! border-zinc-700!">
          <template #title>Zenless Zone Zero</template>
          <template #content>
            <p>
              Character build card, Shiyu Defense summary card, Code redemption,
              and more.
            </p>
          </template>
        </Card>
        <Card class="feature-card bg-zinc-800! border! border-zinc-700!">
          <template #title>Honkai Impact: 3rd</template>
          <template #content>
            <p>Character build card, and more coming soon.</p>
          </template>
        </Card>
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
  padding: 2rem;
}

.top-nav {
  position: absolute;
  top: 1rem;
  right: 1rem;
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

.features {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 2rem;
  max-width: 1200px;
  margin: 0 auto;
}

.feature-card {
  flex: 1 1 300px;
  max-width: 380px;
  min-height: 200px;
  text-align: left;
}

footer {
  color: #666;
  font-size: 0.9rem;
  margin-top: 2rem;
}
</style>
