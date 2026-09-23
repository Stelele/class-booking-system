# Preface

This is the initial document to bootstrap the greenfield project idea to be used in the brainstorming session for context that I find too tedius to put in a chat window. iT will be a living document that will change and grow as the idea gets fleshed out.

# Problem and what I want addressed by this system

I teach a small class of 2 students programming during my free time. in the evenings. The problem is they're both adults working in the UK in the health care industy. one in community care for the nhs another in private care for the elderly. As both are around band 3 so you can imagine that their timetables are pretty busy so scheduling lessons has become a bit of a headache.

To address this issue, in our discussion we agreed to follow this format,

1. At least 1 combined lesson per month at any date that works for the two of them.
2. the the remainder of the weeks, they can book as many one to one sessions as they like with me which have the following features

- I have one lesson per day at around 20h30 Zimbabwe time
- Any one of them can book a slot for everyday except Sunday or other days I've pre emptively marked as not available
- The one to one isn't strict. If for instance they both book the same day its fine, we'll just have a combined session Its really just about booking whenver they are free
- Everyone can see when anyone has booked. It helps in social relatedness so they don't feel like they're isolated or they're getting special treatment and hence feeling bit. Its also a subtle social 'pressure to encourage them to at least always book something because of FOMO

These are the core requirements that need to be available on day 1.

# Features

Features I'd like implemented since I'm already making this up and I think they'll be usefull and fun

- after they've made the booking, I want the system to automatically create a scheduled google meet session at my 20h30 time zone and send the invite to the person that booked it. I own the meeting so I'm automatically added.
- I'd also like to see the meeting on my google calender. Its typically a two hour session. so lets keep it fixed at that
- I'd also like a simple whatsapp message reminder for the students for the following events
  - 1. Give a weekly reminder at the begining of the week (Monday) on what days they have lessons. If they don't have lessons on the week, add a friendly prompt asking them if they want to book any along with a link to book the site
  - 2. A message on the day in the early morning around 8am and 30min before lesson around 20h00
- If they become busy, they should be able to either cancel or reschedule their sessions.

# Tech Stack

## Backend

- Language C# minimal api
- Architecture Clean Architecture. See [dashbord backend](/home/gift/Documents/code-projects/erpnext-dashboard/backend/) for reference on the structure and naming convenstions and code structuring that I like
- Database: SQLite
- ORM: Entity Framework
- Hosting: Digital ocean droplet via docker container
- Whatsapp Messaging provider: Twilio

## Frontend

- Language Vue 3 Typescript
- UI Library Nuxt UI.
- Hosting Digital ocean droplet (same as backend via docker container)
- Architecture: Typical vue framework breakdown (components, views, composables, etc)

## CI/CD

- Repo: Github
- CI: Github actions
- CD: Auto deploy on pushes to main if all tests are passing
- main: prevent pushing directly. Only accept from accepted pull requests
- code rabbit available anything that makes a pr must address its comments

## Repo Structure

|- backend
|- frontend
|- Readme.md
|- License.md
