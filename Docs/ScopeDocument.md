# The Evacuation Plan Scope Document

## Overview of Concept

**The Evacuation Plan** is a simulation game in first-person developed in Unity, where the player is challenged to evacuate a building from an imminent fire in a safe manner. The game focuses on following the protocol to escape a fire in the safest way possible, using a _low poly_ visual style to maintain focus on performance and gameplay.

**Key features:**

- **Fire:** The player will have to evacuate the building in a limited time following the correct path to the meeting point through the emergency escape routes.
- **Limited Visibility:** A dynamic smoke system that accumulates from the ceiling down, forcing the player to crouch to maintain visibility and avoid health loss.
- **Navigation Puzzles:** Blocked paths and "heat-check" door mechanics that force the player to find alternative routes using environmental cues rather than mini-maps.

## Objectives

### Serious Goals

- **Signage Literacy:** Train the player to instinctively look for and follow official emergency exit signs.
- **Protocol Adherence:** Reinforce the "Close the Door" (containment) and "Check for Heat" (prevention) protocols.
- **Spatial Awareness:** Familiarize the player with building layouts navigation and the location of assembly points.

### Measurable Objectives

- **O1:**
  - **Goal:** Ensure the player learns and utilizes the designated emergency escape routes rather than relying on standard exits or prohibited paths (like elevators).
  - **How:** Track the player’s path data. The game will record the number of times the player failed to follow the emergency escape route. A “Evaluation Score” will be shown at the end of each attempt.
- **O2:**
  - **Goal:** Train the player to evacuate the building efficiently within a realistic, critical time frame before the situation becomes lethal.
  - **How:** Implement an internal timer that starts when the fire alarm sounds and stops when the player reaches the meeting point. This timer will be considered in the “Evaluation Score”.
- **O3:**
  - **Goal:** Teach the player to react properly to environmental hazards, such as smoke inhalation or blocked paths.
  - **How:** Track the choices made by the player. For example, measure how long it took for the player to crouch to avoid smoke inhalation while navigating smoke-filled corridors, or track if the player verifies the temperature of closed doors.

### Non-Measurable Objectives

- **Increase Real-World Confidence:** Give a sense of preparedness and calm in the player so that they feel more confident in their ability to react safely if a real-world fire emergency occurs.
- **Spatial Awareness Reinforcement:** Encourage players to proactively look for exit signs and emergency routes in their everyday lives when entering new buildings.

## Description

**The Evacuation Plan** is an evacuation simulator designed for general safety training. It targets a broad audience aiming to improve survival instincts in unfamiliar buildings.

The game is divided into levels of increasing difficulty. In each level, the player starts in a random location. Once the alarm sounds, the goal is to reach the exit. The Low Poly visual style ensures clarity of signs and symbols while maintaining high performance. Players must physically interact with the environment: opening/closing doors, checking for heat, and crouching to stay below the smoke line. If a player makes a fatal mistake, such as opening a hot door or using an elevator, at the end of the simulation an "Evaluation" screen explains the real-world consequences of the user’s decisions. The simulation can end if the player reaches the meeting point, if the player makes so many mistakes that it is lethal to the player, or if the player does not reach the meeting point before the timer ends.

## User Stories (Functional and Non-Functional Requirements)

### First Prototype

- **US001:** The system must implement a first-person controller with walking, crouching, and environmental interaction.
- **US002:** The game must feature a dynamic smoke layer that descends from the ceiling over time.
- **US003:** The system must calculate health loss based on the player’s head position relative to the smoke height.
- **US004:** The game must include interactive doors with a "heat-check" mechanic to detect fire on the other side.
- **US005:** The system must include basic physics for moving or clearing obstacles blocking evacuation routes.
- **US006:** The interface must display a health/breath bar, a timer, and a simple emergency checklist.
- **US007:** A basic ending screen must appear when the player reaches the exit or the health reaches zero.
- **US008:** A main menu with Start, Level Selection, Settings, and Exit buttons.

### Final Prototype

- **US009:** The game must support multiple map layouts with distinct escape paths.
- **US010:** The system must feature wall-mounted "You Are Here" floor plans with a zoom interaction.
- **US011:** The game must track protocol compliance, such as closing doors behind the player to contain fire.
- **US012:** The environment must trigger an "Emergency Mode" with visual and auditory alarms upon the fire event.
- **US013:** The system must block primary routes (elevators/main halls) to force the use of emergency stairs.
- **US014:** The game must present a final Evaluation Screen with evacuation time and protocol error logs.
- **US015:** The UI must be diegetic, integrating maps and documents as physical objects in the game world.
- **US016:** The post-game screen must display a path trace or heat map of the player's movement.

### Non-Functional Requirements

- **US017:** The game must maintain a consistent frame rate (minimum 30 FPS) on mid-range hardware to ensure a smooth simulation experience and prevent motion sickness.
- **US018:** The control scheme and user interface must be intuitive enough for non-gamers to navigate the simulation with a minimal learning curve.
- **US019:** The game must utilize a consistent "Low-Poly" visual identity to ensure that critical emergency elements (signage, fire, exits) are easily identifiable.
- **US020:** The system must use spatialized audio and dynamic lighting to accurately simulate the atmosphere of an emergency (alarms, smoke density, and sirens).
- **US021:** The system must be capable of recording key player metrics (evacuation time and protocol errors) for post-game analysis and educational feedback.
