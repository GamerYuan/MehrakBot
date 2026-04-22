<script setup>
import Card from "primevue/card";
import Accordion from "primevue/accordion";
import AccordionPanel from "primevue/accordionpanel";
import AccordionHeader from "primevue/accordionheader";
import AccordionContent from "primevue/accordioncontent";

const faqItems = [
  {
    question: "How do I use Mehrak?",
    answer: "You can use Mehrak with any of the specified commands in the ",
    link: { text: "Commands", href: "https://gameryuan.gitbook.io/mehrak/commands" },
    answerEnd: " section",
  },
  {
    question: "How do I add a profile on Mehrak?",
    answer: "You can refer to the ",
    link: { text: "Profile Commands", href: "https://gameryuan.gitbook.io/mehrak/commands/common-commands/profile" },
    answerEnd: " suite on how to manage your profiles with Mehrak",
  },
  {
    question: "How can I remove my user data on Mehrak?",
    answer: "When you remove your last available profile, Mehrak will automatically remove all stored user data you have with Mehrak, including your Discord ID, HoYoLAB UID and Cookies, except for the collected usage metrics. The usage metrics only contains the commands that you have executed when you use Mehrak",
  },
  {
    question: "How do I use Mehrak outside of my Discord server?",
    answer: "By installing Mehrak to your user profile as a User Application, you can use Mehrak in any servers with External Applications enabled, or in DMs. You may refer to ",
    link: { text: "Getting Started", href: "https://gameryuan.gitbook.io/mehrak/getting-started" },
  },
  {
    question: "Is Mehrak safe?",
    answer: "Mehrak is committed to provide maximum security for our users. You can read more about this under the ",
    link: { text: "Security", href: "https://gameryuan.gitbook.io/mehrak/resources/security" },
    answerEnd: " Page",
  },
  {
    question: "Some commands are not working!",
    answer: "Please try again. If it's still not working, contact a developer in our ",
    link: { text: "Official Discord Server", href: "https://discord.gg/3GYvRD4u9a" },
  },
];
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <h2 class="text-3xl font-bold tracking-tight text-zinc-100 mb-2">
          FAQ
        </h2>
        <p class="text-zinc-300 leading-relaxed">
          Frequently asked questions about Mehrak
        </p>
      </template>
    </Card>

    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <Accordion value="" multiple expandIcon="pi pi-chevron-down" collapseIcon="pi pi-chevron-up">
          <AccordionPanel v-for="(item, index) in faqItems" :key="index" :value="index">
            <AccordionHeader>{{ item.question }}</AccordionHeader>
            <AccordionContent>
              <p class="text-zinc-300 leading-relaxed m-0">
                {{ item.answer
                }}<a
                  v-if="item.link"
                  :href="item.link.href"
                  target="_blank"
                  rel="noopener noreferrer"
                  class="text-emerald-400 hover:text-emerald-300 transition-colors"
                  >{{ item.link.text }}</a
                >{{ item.answerEnd || "" }}
              </p>
            </AccordionContent>
          </AccordionPanel>
        </Accordion>
      </template>
    </Card>
  </div>
</template>
