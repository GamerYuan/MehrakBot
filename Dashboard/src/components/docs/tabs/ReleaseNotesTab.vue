<script setup>
import { ref } from "vue";
import Card from "primevue/card";

const releases = [
  {
    version: "v1.2.0",
    date: "January 15, 2026",
    notes: [
      {
        type: "feature",
        text: "Added support for Zenless Zone Zero",
      },
      {
        type: "feature",
        text: "New exploration progress command",
      },
      {
        type: "improvement",
        text: "Improved cookie encryption performance",
      },
      {
        type: "fix",
        text: "Fixed real-time notes not updating for some users",
      },
    ],
  },
  {
    version: "v1.1.0",
    date: "December 20, 2025",
    notes: [
      {
        type: "feature",
        text: "Added profile management commands",
      },
      {
        type: "feature",
        text: "New character build viewer",
      },
      {
        type: "improvement",
        text: "Redesigned command responses for better readability",
      },
      {
        type: "fix",
        text: "Fixed authentication timeout issues",
      },
    ],
  },
  {
    version: "v1.0.0",
    date: "November 1, 2025",
    notes: [
      {
        type: "feature",
        text: "Initial release of Mehrak",
      },
      {
        type: "feature",
        text: "Support for Genshin Impact and Honkai: Star Rail",
      },
      {
        type: "feature",
        text: "Real-time notes (Resin, Stamina, etc.)",
      },
      {
        type: "feature",
        text: "Code redemption support",
      },
    ],
  },
];

const selectedVersion = ref(releases[0].version);

const scrollToVersion = (version) => {
  selectedVersion.value = version;
  const element = document.getElementById(`release-${version}`);
  if (element) {
    element.scrollIntoView({ behavior: "smooth", block: "start" });
  }
};

const getTypeLabel = (type) => {
  const labels = {
    feature: { label: "Feature", class: "bg-emerald-500/20 text-emerald-300" },
    improvement: { label: "Improvement", class: "bg-blue-500/20 text-blue-300" },
    fix: { label: "Fix", class: "bg-orange-500/20 text-orange-300" },
  };
  return labels[type] || { label: type, class: "bg-zinc-500/20 text-zinc-300" };
};
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <h2 class="text-3xl font-bold tracking-tight text-zinc-100 mb-2">
          Release Notes
        </h2>
        <p class="text-zinc-300 leading-relaxed">
          Track important documentation updates and behavior changes between bot releases
        </p>
      </template>
    </Card>

    <div class="grid grid-cols-1 lg:grid-cols-[200px_1fr] gap-6">
      <aside class="lg:sticky lg:top-28 h-fit">
        <Card class="bg-white/5 border border-white/10 rounded-2xl">
          <template #content>
            <h4 class="text-sm font-semibold text-zinc-400 uppercase tracking-wider mb-3">
              Versions
            </h4>
            <nav class="flex flex-col gap-1">
              <button
                v-for="release in releases"
                :key="release.version"
                type="button"
                :class="[
                  'text-left px-3 py-2 rounded-lg text-sm transition-all',
                  selectedVersion === release.version
                    ? 'text-white bg-emerald-500/20 border border-emerald-500/50'
                    : 'text-zinc-400 hover:text-zinc-200 hover:bg-white/5'
                ]"
                @click="scrollToVersion(release.version)"
              >
                <span class="font-mono">{{ release.version }}</span>
              </button>
            </nav>
          </template>
        </Card>
      </aside>

      <div class="flex flex-col gap-4">
        <Card
          v-for="release in releases"
          :key="release.version"
          :id="`release-${release.version}`"
          class="bg-white/5 border border-white/10 rounded-2xl"
        >
          <template #content>
            <div class="flex flex-col gap-4">
              <div class="flex items-center justify-between">
                <h3 class="text-xl font-bold text-zinc-100 font-mono">
                  {{ release.version }}
                </h3>
                <span class="text-sm text-zinc-500">{{ release.date }}</span>
              </div>

              <ul class="flex flex-col gap-3">
                <li
                  v-for="(note, index) in release.notes"
                  :key="index"
                  class="flex items-start gap-3"
                >
                  <span
                    :class="[
                      'shrink-0 text-xs font-semibold px-2 py-1 rounded',
                      getTypeLabel(note.type).class
                    ]"
                  >
                    {{ getTypeLabel(note.type).label }}
                  </span>
                  <span class="text-zinc-300 leading-relaxed">{{ note.text }}</span>
                </li>
              </ul>
            </div>
          </template>
        </Card>
      </div>
    </div>
  </div>
</template>
