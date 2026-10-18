using System;

namespace Deadlock;

/// <summary>
/// Plays as a Deadlock hero: spawns at one of the map's spawn points, moves the way the game does with the hero's own
/// numbers from VData (run, out of combat sprint, crouch, jump and air jump, ground and air dash with stamina, dash
/// jump, slide, mantle), animates with the hero's AnimGraph 2 graph through <see cref="DeadlockAnimator"/> and drives
/// the camera over the shoulder.
/// </summary>
/// <remarks>
/// Controls are the project's input actions: move, Jump, Run to dash, Duck to crouch and slide, Attack1/Attack2 to
/// leave out of combat for <see cref="OutOfCombatDelay"/>. Put it on its own GameObject next to a
/// <see cref="DeadlockMap"/>; it builds its body, a <see cref="CharacterController"/> and drives the camera when play
/// starts.
/// </remarks>
[Title( "Deadlock Hero" ), Category( "Deadlock" ), Icon( "sports_martial_arts" )]
public sealed class DeadlockHero : Component
{
	public enum HeroMoveState
	{
		/// <summary>Running, crouching, jumping and falling.</summary>
		Normal,
		GroundDash,
		AirDash,
		Slide,
		Mantle,
	}

	/// <summary>
	/// Tag given to map colliders a hero walks through in game (foliage, line of sight and NPC blockers), which the
	/// hero's controller then ignores.
	/// </summary>
	public const string PassableTag = "deadlock_passable";

	/// <summary>
	/// Collision tags of map colliders that block heroes. Map colliders tagged with none of these don't.
	/// </summary>
	private static readonly string[] BlockingTags = ["solid", "contents_solid", "playerclip", "window", "passbullets", "sky", "citadel_skyclip"];

	/// <summary>
	/// The hero, by its name in scripts/heroes.vdata: hero_drifter, hero_inferno... Changing it while playing swaps
	/// the hero where it stands.
	/// </summary>
	[Property, Editor( "deadlock_hero" )] public string Hero { get; set; } = "hero_drifter";

	/// <summary>
	/// Whether the hero starts at one of the map's spawn points rather than where this GameObject is.
	/// </summary>
	[Property] public bool StartAtSpawnPoint { get; set; } = true;

	/// <summary>
	/// The camera that follows the hero. Unset, the scene's main camera.
	/// </summary>
	[Property] public CameraComponent Camera { get; set; }

	/// <summary>
	/// Seconds after Attack1 or Attack2 before the hero is out of combat again, with the relaxed animations and the
	/// sprint bonus. The game counts damage dealt and taken instead, which nothing here does.
	/// </summary>
	[Property, Group( "Combat" )] public float OutOfCombatDelay { get; set; } = 4f;

	// Deadlock's movement convars, at their defaults in server.dll: sv_gravity, sv_friction, sv_stopspeed.
	[Property, Group( "Movement" )] public float Gravity { get; set; } = 800f;
	[Property, Group( "Movement" )] public float Friction { get; set; } = 4f;
	[Property, Group( "Movement" )] public float StopSpeed { get; set; } = 100f;
	[Property, Group( "Movement" )] public float Radius { get; set; } = 16f;
	[Property, Group( "Movement" )] public float StandingHeight { get; set; } = 72f;
	[Property, Group( "Movement" )] public float CrouchingHeight { get; set; } = 48f;

	/// <summary>
	/// How far the camera turns from a standing hero before it turns in place (playing its turn animation).
	/// </summary>
	[Property, Group( "Movement" )] public float TurnInPlaceAngle { get; set; } = 70f;

	/// <summary>
	/// How fast the body turns to face where the camera looks while moving, in degrees a second.
	/// </summary>
	[Property, Group( "Movement" )] public float BodyTurnSpeed { get; set; } = 720f;

	// The camera, with Deadlock's own defaults (its citadel_camera_override_* convars): a pivot above the hero's
	// feet, the camera behind and right of it.
	/// <summary>
	/// For testing: stamina never runs out.
	/// </summary>
	[Property, Group( "Testing" )] public bool InfiniteStamina { get; set; } = true;

	/// <summary>
	/// Draws a crosshair and the stamina pips under it.
	/// </summary>
	[Property, Group( "Testing" )] public bool ShowHud { get; set; }

	// More of Deadlock's movement convars (constants, so hot reloads keep them).
	/// <summary>sv_airaccelerate: how fast air control reaches its speed, times the wished speed, a second.</summary>
	private const float AirAccelerate = 10f;

	/// <summary>
	/// citadel_aircontrol_speed_slow and _fast: the most speed air control adds along the keys, falling from the first
	/// to the second as the hero goes from 600 to 800 units/s across the ground.
	/// </summary>
	private const float AirControlSpeedSlow = 80f;
	private const float AirControlSpeedFast = 50f;
	private const float AirControlSlowUntilSpeed = 600f;
	private const float AirControlFastFromSpeed = 800f;

	/// <summary>citadel_air_drag_min: the air drag there always is, like friction in the air.</summary>
	private const float AirDragMin = 0.2f;

	/// <summary>citadel_min_accel_speed: ground acceleration is at least as hard as toward this speed.</summary>
	private const float MinAccelSpeed = 400f;

	/// <summary>
	/// citadel_wall_jump_num_free_wall_jumps, citadel_initial_wall_jump_stamina_cost and
	/// citadel_wall_jump_stamina_cost: the first wall jumps in the air are free, the next ones cost stamina.
	/// </summary>
	private const int FreeWallJumps = 1;
	private const float InitialWallJumpStaminaCost = 0f;
	private const float WallJumpStaminaCost = 0.5f;

	[Property, Group( "Camera" )] public float CameraDistance { get; set; } = 135f;
	[Property, Group( "Camera" )] public float CameraHeight { get; set; } = 86f;
	[Property, Group( "Camera" )] public float CameraCrouchingHeight { get; set; } = 55f;
	[Property, Group( "Camera" )] public float CameraShoulder { get; set; } = 34f;
	[Property, Group( "Camera" )] public float CameraFieldOfView { get; set; } = 90f;

	/// <summary>
	/// Where the hero looks: the camera's angles.
	/// </summary>
	public Angles EyeAngles { get; set; }

	public DeadlockHeroData Data { get; private set; }
	public HeroMoveState MoveState { get; private set; }
	public float Stamina { get; private set; }
	public bool IsCrouching { get; private set; }
	public bool IsOutOfCombat => combatTime <= 0f;
	public CharacterController Controller => controller;
	public SkinnedModelRenderer Body => body;
	public DeadlockAnimator Animator => animator;

	private CharacterController controller;
	private SkinnedModelRenderer body;
	private DeadlockAnimator animator;
	private HeroInput input;
	private string loadedHero;

	private float bodyYaw;
	private float bodyYawRate;
	private float crouchFraction;
	private float combatTime;
	private float timeSinceGrounded;
	private bool jumpedSinceGrounded;
	private bool airJumped;
	private bool airDashed;
	private int wallJumps;
	private float timeOnGround;
	private bool wasMoving;

	// Slide jumps in a row (the hero not standing on the ground for the slide's air drag reset time between them),
	// and since when the last slide ended: from the second, the slide's air drag slows the hero after the jump.
	private int slideJumps;
	private float timeSinceSlideEnd = float.MaxValue;

	// After a dash jump, air control is stronger for a moment.
	private float dashJumpAirControlTime;
	private float turnTime;

	// The base_action the graph sees for one update (an action, like ground_jump), rather than a held state.
	private string pendingAction;

	private Vector3 dashDirection;
	private float dashTime;
	private float dashDuration;
	private float dashDistance;
	private float timeSinceDash = float.MaxValue;

	// Where an air dash started (it ends once it's covered its distance), and since when the last one ended: its
	// post drag slows a hero still going very fast right after.
	private Vector3 airDashStart;
	private float timeSinceAirDash = float.MaxValue;

	// How fast the hero has recently been moving (decaying), what a mantle carries over the ledge, and since when.
	private float recentSpeed;
	private float mantleSpeed;
	private float timeSinceMantle = float.MaxValue;

	private float slideTime;
	private float slideDashFrictionTime;
	private float slideCooldown;
	private bool slideBuffered;

	private float timeSinceWallJump = float.MaxValue;
	private float returnToWallTime;

	private enum MeleePhase
	{
		None,
		Holding,
		Charging,
		Attacking,
	}

	private MeleePhase meleePhase;
	private float meleeTime;
	private float meleeCooldown;
	private bool meleeSecondSwing;
	private bool meleeHeavy;
	private bool meleeAir;
	private bool meleeHit;
	private Vector3 meleeDirection;
	private DeadlockHeroData.MeleeAttack meleeAttack;
	private DeadlockParticleEffect meleeChargeEffect;
	private Vector3 wallJumpNormal;
	private float staminaPenaltyTime;
	private string wallAttach = "";

	// Movement particles: the ones following the hero, and every one still playing out.
	private DeadlockParticleEffect slideEffect;
	private DeadlockParticleEffect dashEffect;
	private readonly List<MovementEffect> movementEffects = [];

	private sealed class MovementEffect
	{
		public DeadlockParticleEffect Effect;
		public float Age;

		/// <summary>The model attachment control point 1 follows, if any.</summary>
		public string Attachment;
		public GameObject Body;
	}

	private DeadlockHeroData.MantleType mantleType;
	private Vector3 mantleStart;
	private Vector3 mantleDirection;
	private float mantleRise;
	private float mantleForward;
	private float mantleTime;
	private float mantlePushTime;

	/// <summary>
	/// What the player does in one update: the input actions, or anything else driving the hero.
	/// </summary>
	public struct HeroInput
	{
		/// <summary>Movement keys: x forward, y left, each -1 to 1.</summary>
		public Vector2 Move;

		/// <summary>How far the camera turns.</summary>
		public Angles Look;

		public bool Jump;
		public bool Dash;
		public bool DuckPressed;
		public bool DuckHeld;
		public bool Attack;
		public bool MeleePressed;
		public bool MeleeHeld;

		/// <summary>
		/// This frame's input actions: move, Jump, Run (dash), Duck (crouch and slide), Attack1 and Attack2.
		/// </summary>
		public static HeroInput FromActions() => new()
		{
			Move = new Vector2( Input.AnalogMove.x, Input.AnalogMove.y ),
			Look = Input.AnalogLook,
			Jump = Input.Pressed( "Jump" ),
			Dash = Input.Pressed( "Run" ),
			DuckPressed = Input.Pressed( "Duck" ),
			DuckHeld = Input.Down( "Duck" ),
			Attack = Input.Pressed( "Attack1" ) || Input.Pressed( "Attack2" ),
			MeleePressed = Input.Pressed( "Melee" ),
			MeleeHeld = Input.Down( "Melee" ),
		};
	}

	protected override void OnStart()
	{
		Spawn();
	}

	/// <summary>
	/// Loads the hero, builds its body and controller and puts it at a spawn point. Play mode does this on start.
	/// </summary>
	public void Spawn()
	{
		controller = Components.GetOrCreate<CharacterController>();
		controller.Radius = Radius;
		controller.Height = StandingHeight;
		controller.IgnoreLayers.Add( PassableTag );
		MarkPassableMapColliders();

		EyeAngles = new Angles( 0f, WorldRotation.Yaw(), 0f );

		if ( StartAtSpawnPoint )
			MoveToSpawnPoint();

		WorldRotation = Rotation.Identity;
		bodyYaw = EyeAngles.yaw;

		LoadHero();
	}

	/// <summary>
	/// Loads <see cref="Hero"/>'s data and builds its body in place of the last one.
	/// </summary>
	private void LoadHero()
	{
		loadedHero = Hero;

		if ( body.IsValid() )
			body.GameObject.Destroy();

		body = null;
		animator = null;
		StopEffect( ref slideEffect );
		StopEffect( ref dashEffect );
		StopEffect( ref meleeChargeEffect );
		meleePhase = MeleePhase.None;
		MoveState = HeroMoveState.Normal;
		pendingAction = null;

		Data = DeadlockHeroData.Load( Hero );
		if ( Data is null )
		{
			Log.Warning( $"{GameObject.Name}: no hero '{Hero}' in Deadlock's scripts/heroes.vdata. Is the Deadlock mount enabled?" );
			return;
		}

		Stamina = Data.Stamina;
		CreateBody();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy || controller is null )
			return;

		if ( Hero != loadedHero )
			LoadHero();

		Simulate( HeroInput.FromActions(), Time.Delta );
		UpdateCamera();
		DrawHud();
	}

	/// <summary>
	/// Moves and animates the hero for one update with this input. Play mode does this every frame with the
	/// player's input actions.
	/// </summary>
	public void Simulate( HeroInput input, float dt )
	{
		if ( controller is null || Data is null || dt <= 0f )
			return;

		this.input = input;
		Look();
		var wishDirection = WishDirection();

		Tick( dt );
		HandleActions( wishDirection );
		UpdateMelee( dt );
		UpdateCrouch();
		Move( wishDirection, dt );
		UpdateFacing( wishDirection, dt );
		UpdateWallAttach( wishDirection );
		Animate( wishDirection, dt );
		UpdateEffects( dt );
	}

	private void CreateBody()
	{
		var model = Model.Load( $"mount://deadlock/{Data.ModelName}" );

		var bodyObject = new GameObject( true, "Body" );
		bodyObject.SetParent( GameObject, false );

		body = bodyObject.Components.Create<SkinnedModelRenderer>();
		body.Model = model;

		animator = bodyObject.Components.Create<DeadlockAnimator>();
		animator.Renderer = body;
		animator.UpdateAutomatically = false;
		animator.Graph = FindGraph( model, Data.ModelName );
	}

	/// <summary>
	/// The graph a hero model plays: the first it lists. Models imported before the mount recorded their graphs
	/// (until s&amp;box restarts) get a guess from the model's name, which is right for most heroes.
	/// </summary>
	private static DeadlockAnimationGraph FindGraph( Model model, string modelName )
	{
		if ( model.IsValid() && model.TryGetData<DeadlockAnimGraphs>( out var graphs ) && graphs?.Graphs is { Count: > 0 } references
			&& DeadlockAnimationGraph.Find( references[0].Graph ) is { } graph )
			return graph;

		var name = modelName.Replace( '\\', '/' );
		name = name[(name.LastIndexOf( '/' ) + 1)..];
		if ( name.EndsWith( ".vmdl" ) )
			name = name[..^5];

		return DeadlockAnimationGraph.Find( $"animgraphs/animgraph2/hero/hero.vnmgraph+{name}.vnmgraph" );
	}

	/// <summary>
	/// Map colliders heroes walk through in game get <see cref="PassableTag"/>: the mount keeps each collision
	/// attribute's tags, and only some of them block players.
	/// </summary>
	private void MarkPassableMapColliders()
	{
		foreach ( var map in Scene.GetAllComponents<DeadlockMap>() )
		{
			foreach ( var collider in map.Components.GetAll<Collider>( FindMode.EverythingInDescendants ) )
			{
				var tags = collider.GameObject.Tags;
				if ( collider.IsTrigger || !tags.TryGetAll().Any() )
					continue;

				if ( !BlockingTags.Any( tags.Has ) )
					tags.Add( PassableTag );
			}
		}
	}

	private void MoveToSpawnPoint()
	{
		var spawns = Scene.GetAllComponents<SpawnPoint>().ToList();
		if ( spawns.Count == 0 )
			return;

		var spawn = spawns[Game.Random.Next( spawns.Count )];
		WorldPosition = spawn.WorldPosition;
		EyeAngles = new Angles( 0f, spawn.WorldRotation.Yaw(), 0f );
	}

	private void Look()
	{
		var angles = EyeAngles + input.Look;
		angles.pitch = Math.Clamp( angles.pitch, -89f, 89f );
		angles.roll = 0f;
		EyeAngles = angles;
	}

	/// <summary>
	/// The direction the movement keys ask for, flat, relative to where the camera looks; zero for none.
	/// </summary>
	private Vector3 WishDirection()
	{
		var move = input.Move;
		var wish = Rotation.FromYaw( EyeAngles.yaw ) * new Vector3( move.x, move.y, 0f );
		return wish.Length > 0.01f ? wish.Normal : Vector3.Zero;
	}

	private float GroundSpeed()
	{
		var speed = IsCrouching ? Data.CrouchSpeed : Data.MaxMoveSpeed + (IsOutOfCombat ? Data.SprintSpeed : 0f);
		return meleePhase == MeleePhase.Charging ? MathF.Min( speed, Data.MeleeChargeMoveSpeed ) : speed;
	}

	private void Tick( float dt )
	{
		// A wall jump slows stamina regeneration for a while.
		var regen = Data.StaminaRegenPerSecond * (staminaPenaltyTime > 0f ? 1f - Data.WallJumpStaminaRegenReduction : 1f);
		Stamina = MathF.Min( Data.Stamina, Stamina + regen * dt );
		staminaPenaltyTime -= dt;
		timeSinceWallJump += dt;
		returnToWallTime -= dt;
		timeSinceMantle += dt;

		var speed = controller.Velocity.WithZ( 0f ).Length;
		recentSpeed = MathF.Max( speed, recentSpeed * MathF.Max( 0f, 1f - 4f * dt ) );
		meleeCooldown -= dt;

		if ( InfiniteStamina )
			Stamina = Data.Stamina;
		combatTime -= dt;
		slideCooldown -= dt;
		timeSinceDash += dt;
		timeSinceAirDash += dt;
		timeSinceSlideEnd += dt;
		dashJumpAirControlTime -= dt;

		if ( controller.IsOnGround )
		{
			timeSinceGrounded = 0f;
			jumpedSinceGrounded = false;
			airJumped = false;
			airDashed = false;
			wallJumps = 0;

			// Standing on the ground long enough ends a run of slide jumps.
			timeOnGround += dt;
			if ( timeOnGround > Data.SlideAirDragResetTime )
				slideJumps = 0;
		}
		else
		{
			timeSinceGrounded += dt;
			timeOnGround = 0f;
		}
	}

	private void HandleActions( Vector3 wishDirection )
	{
		if ( input.Attack )
			combatTime = OutOfCombatDelay;

		if ( MoveState == HeroMoveState.Mantle || meleePhase == MeleePhase.Charging || MeleeLunging )
			return;

		if ( input.Dash )
			TryDash( wishDirection );

		if ( input.Jump )
			Jump( wishDirection );

		// Crouch is held in Deadlock: a slide starts as soon as it can while it is (mid dash, once dash slides
		// unlock, or when the hero gets fast enough). A press during a dash also counts until the dash can slide.
		if ( input.DuckPressed )
			slideBuffered = MoveState == HeroMoveState.GroundDash;

		if ( MoveState != HeroMoveState.GroundDash && timeSinceDash > Data.DashSlideWindow )
			slideBuffered = false;

		if ( input.DuckHeld || slideBuffered )
			TrySlide();
	}

	private void TryDash( Vector3 wishDirection )
	{
		if ( MoveState is HeroMoveState.GroundDash or HeroMoveState.AirDash || Stamina < 1f )
			return;

		// With no keys held, the hero dashes back, away from where it looks.
		var direction = wishDirection.Length > 0f ? wishDirection : Rotation.FromYaw( EyeAngles.yaw ).Backward;
		var grounded = controller.IsOnGround || MoveState == HeroMoveState.Slide;

		// One air dash until the hero lands.
		if ( !grounded && airDashed )
			return;

		Stamina -= 1f;
		dashDirection = direction;
		dashTime = 0f;

		StopEffect( ref dashEffect );
		dashEffect = PlayEffect( Data.DashParticle, WorldPosition, Rotation.LookAt( direction ), true, values: new Vector3( StandingHeight, 0f, 0f ) );

		if ( grounded )
		{
			MoveState = HeroMoveState.GroundDash;
			dashDuration = Data.GroundDashDuration;
			dashDistance = Data.GroundDashDistance;

			// A ground dash starts the hero from a standstill: it covers its distance whatever the hero was doing.
			controller.Velocity = controller.Velocity.WithX( 0f ).WithY( 0f );
			Trigger( "Ground_Dash" );
		}
		else
		{
			// An air dash doesn't set the hero's speed: it raises air control's cap to the dash speed, falling over
			// the dash, and steers toward it (MoveAirDash).
			MoveState = HeroMoveState.AirDash;
			dashDuration = Data.AirDashDuration;
			dashDistance = Data.AirDashDistance;
			airDashStart = WorldPosition;
			airDashed = true;
			Trigger( AirDashAction( direction ) );
		}
	}

	/// <summary>
	/// The air dash animation for a direction: forward, back, left or right of the body.
	/// </summary>
	private string AirDashAction( Vector3 direction ) => "air_dash_" + DirectionName( direction );

	private void Jump( Vector3 wishDirection )
	{
		var velocity = controller.Velocity;
		var horizontal = velocity.WithZ( 0f );

		switch ( MoveState )
		{
			case HeroMoveState.GroundDash:
			{
				MoveState = HeroMoveState.Normal;
				timeSinceDash = 0f;
				StopEffect( ref dashEffect );

				if ( dashTime >= Data.DashJumpStartTime && dashTime <= Data.DashJumpEndTime )
				{
					// A dash jump: the distance over the jump's own airtime.
					var airtime = 2f * Data.DashJumpSpeed / MathF.Max( Gravity, 1f );
					Launch( dashDirection * (Data.DashJumpDistance / airtime), Data.DashJumpSpeed );
					dashJumpAirControlTime = Data.DashJumpExtraAirControlTime;
					Trigger( "dash_jump" );
					PlayEffect( Data.DashJumpParticle, WorldPosition, Rotation.LookAt( dashDirection ), false );
					return;
				}

				Launch( horizontal.ClampLength( Data.DashJumpMissMaxSpeed ), Data.JumpSpeed );
				Trigger( "ground_jump" );
				return;
			}

			case HeroMoveState.Slide:
			{
				var speed = horizontal.Length;
				if ( slideTime < Data.SlideLeapSpeedPenaltyTime && speed > 0f )
				{
					var penalty = Data.SlideLeapSpeedPenaltyMax * (1f - slideTime / Data.SlideLeapSpeedPenaltyTime);
					horizontal *= MathF.Max( 0f, speed - penalty ) / speed;
				}

				EndSlide( false );
				slideJumps++;
				Launch( horizontal, Data.JumpSpeed );
				Trigger( "ground_jump" );
				return;
			}

			case HeroMoveState.AirDash:
				return;
		}

		// In the air next to a wall, jumping jumps off it, while the stamina lasts; the first is free.
		if ( !controller.IsOnGround && TryWallJump( wishDirection ) )
			return;

		if ( controller.IsOnGround || (timeSinceGrounded <= Data.LateJumpGraceWindow && !jumpedSinceGrounded) )
		{
			// Jumping just after a slide ended still counts as a slide jump.
			if ( timeSinceSlideEnd <= Data.LateSlideJumpWindow )
				slideJumps++;

			Launch( horizontal, Data.JumpSpeed );
			Trigger( "ground_jump" );
			return;
		}

		if ( !airJumped && Stamina >= 1f )
		{
			// Deadlock's air jump: speed toward the keys, up to the hero's max speed along them and by at most the max
			// speed delta, keeping the rest of its momentum; and the jump's full upward speed, however fast the hero
			// was rising or falling.
			Stamina -= 1f;
			airJumped = true;

			if ( wishDirection.Length > 0f )
			{
				var add = MathF.Min( GroundSpeed() - horizontal.Dot( wishDirection ), Data.AirJumpMaxSpeedDelta );
				if ( add > 0f )
					horizontal += wishDirection * add;
			}

			Launch( horizontal, Data.JumpSpeed );
			Trigger( "air_jump" );
			PlayEffect( Data.AirJumpParticle, WorldPosition, Rotation.FromYaw( bodyYaw ), false );
		}
	}

	/// <summary>
	/// Jumps off a wall the hero is in the air next to: out from it, keeping the speed along it plus a little toward
	/// where the keys point, and up. The upward push recovers from fatigue over the full power recharge time, so
	/// chained wall jumps climb less.
	/// </summary>
	private bool TryWallJump( Vector3 wishDirection )
	{
		// The first wall jumps in a jump are free, the next ones cost stamina; either way the hero needs some left.
		var cost = wallJumps < FreeWallJumps ? 0f : wallJumps >= 1 ? WallJumpStaminaCost : InitialWallJumpStaminaCost;
		if ( Stamina - cost <= 0f || !FindWall( out var normal, out var point ) )
			return false;

		Stamina -= cost;
		wallJumps++;

		// Fatigue: power recovers over the recharge time from the minimum, along the power bias (0 is aggressive,
		// full power early; 0.5 linear; 1 gentle). It only scales the upward speed.
		var recharge = Math.Clamp( timeSinceWallJump / MathF.Max( Data.WallJumpFullPowerRechargeTime, 0.01f ), 0f, 1f );
		var power = MathX.Lerp( Data.WallJumpPowerMin, 1f, Bias( recharge, 1f - Data.WallJumpPowerBias ) );

		// Out from the wall at the min out speed when the keys point into it, the max when they point away.
		var away = wishDirection.Length > 0f ? (wishDirection.Dot( normal ) + 1f) * 0.5f : 0.5f;
		var along = Vector3.Cross( normal, Vector3.Up ).Normal;
		var alongSpeed = controller.Velocity.Dot( along );
		var lateral = wishDirection.Dot( along ) * Data.WallJumpMaxLateralSpeed * Data.WallJumpLateralFalloff.Evaluate( MathF.Abs( alongSpeed ) );
		var horizontal = normal * MathX.Lerp( Data.WallJumpMinOutSpeed, Data.WallJumpMaxOutSpeed, away ) + along * (alongSpeed + lateral);

		timeSinceWallJump = 0f;
		returnToWallTime = Data.WallJumpLateralInputSuppressTime;
		staminaPenaltyTime = Data.WallJumpStaminaRegenReductionDuration;
		wallJumpNormal = normal;

		Launch( horizontal, Data.WallJumpUpSpeed * power );
		Trigger( "wall_jump_" + DirectionName( horizontal ) );

		var facing = Rotation.LookAt( horizontal.Length > 1f ? horizontal : normal );
		PlayEffect( Data.WallJumpParticle, point, facing, false, new Transform( point, Rotation.LookAt( normal ) ) );
		return true;
	}

	/// <summary>
	/// The nearest wall within a wall jump's reach of the hero, a reach that grows with the hero's speed.
	/// </summary>
	private bool FindWall( out Vector3 normal, out Vector3 point )
	{
		normal = default;
		point = default;

		var reach = Data.WallJumpReach;
		var speed = controller.Velocity.WithZ( 0f ).Length;
		var distance = MathX.Lerp( reach.MinDistance, reach.MaxDistance, InverseLerp( reach.MinSpeed, reach.MaxSpeed, speed ) );

		var hull = controller.BoundingBox;
		var start = WorldPosition + Vector3.Up * 2f;
		var nearest = float.MaxValue;

		for ( var i = 0; i < 8; i++ )
		{
			var direction = Rotation.FromYaw( i * 45f ).Forward;
			var trace = Scene.Trace.Box( hull, start, start + direction * distance )
				.IgnoreGameObjectHierarchy( GameObject )
				.WithoutTags( PassableTag )
				.Run();

			if ( !trace.Hit || trace.StartedSolid || MathF.Abs( trace.Normal.z ) > 0.7f || trace.Distance >= nearest )
				continue;

			nearest = trace.Distance;
			normal = trace.Normal.WithZ( 0f ).Normal;
			point = trace.HitPosition;
		}

		return nearest < float.MaxValue;
	}

	/// <summary>
	/// The wall attach pose (<c>base_state</c> wall_attach_forward, _left or _right) while the hero is in the air
	/// pushing into a wall it could jump off.
	/// </summary>
	private void UpdateWallAttach( Vector3 wishDirection )
	{
		wallAttach = "";

		if ( controller.IsOnGround || MoveState != HeroMoveState.Normal || wishDirection.Length <= 0f || !FindWall( out var normal, out _ ) )
			return;

		var into = -normal;
		if ( wishDirection.Dot( into ) < 0.5f )
			return;

		var facing = Rotation.FromYaw( bodyYaw );
		if ( into.Dot( facing.Forward ) > 0.7f )
			wallAttach = "wall_attach_forward";
		else if ( into.Dot( facing.Forward ) > -0.7f )
			wallAttach = into.Dot( facing.Left ) > 0f ? "wall_attach_left" : "wall_attach_right";
	}

	/// <summary>
	/// A direction relative to the body, as the graph's directional actions name it: forward, back, left or right.
	/// </summary>
	private string DirectionName( Vector3 direction )
	{
		// The game's split: within 30 degrees of the body's facing is forward, beyond 135 back, the rest the sides.
		var angle = DeltaAngle( bodyYaw, YawOf( direction ) );

		if ( MathF.Abs( angle ) <= 30f )
			return "forward";

		if ( MathF.Abs( angle ) >= 135f )
			return "back";

		return angle > 0f ? "left" : "right";
	}

	/// <summary>
	/// The bias curve Source uses: 0 and 1 stay put, and the bias is what 0.5 becomes.
	/// </summary>
	private static float Bias( float x, float bias )
		=> bias is <= 0f or >= 1f ? x : MathF.Pow( x, MathF.Log( bias ) / MathF.Log( 0.5f ) );

	/// <summary>
	/// Leaves the ground with this horizontal velocity and upward speed.
	/// </summary>
	private void Launch( Vector3 horizontal, float upSpeed )
	{
		jumpedSinceGrounded = true;
		controller.Velocity = horizontal.WithZ( 0f );
		controller.Punch( Vector3.Up * upSpeed );
	}

	private void TrySlide()
	{
		if ( MoveState == HeroMoveState.GroundDash )
		{
			// A dash turns into a dash slide once it's run for the dash slide start time, still going faster than
			// the fail speed.
			if ( dashTime < Data.SlideDashStartTime || !controller.IsOnGround )
				return;

			if ( controller.Velocity.WithZ( 0f ).Length >= Data.SlideDashFailSpeed )
			{
				StartSlide( dashDirection * Data.SlideDashSpeed, Data.SlideDashFrictionTime );
				return;
			}
		}
		else if ( MoveState != HeroMoveState.Normal || !controller.IsOnGround || slideCooldown > 0f )
		{
			return;
		}

		var horizontal = controller.Velocity.WithZ( 0f );
		var current = horizontal.Length;
		var afterDash = MoveState == HeroMoveState.GroundDash || timeSinceDash <= Data.DashSlideWindow || timeSinceMantle <= MantleSlideWindow;

		if ( current >= Data.SlideMinActivationSpeed || (afterDash && current >= Data.SlideDashMinActivationSpeed) || FacingDownSlope( horizontal ) )
			StartSlide( horizontal, afterDash ? Data.SlideDashFrictionTime : 0f );
	}

	/// <summary>
	/// Whether the hero is moving down a slope steep enough to slide on from any speed.
	/// </summary>
	private bool FacingDownSlope( Vector3 horizontal )
	{
		if ( horizontal.Length < 1f )
			return false;

		var normal = GroundNormal();
		if ( Vector3.GetAngle( Vector3.Up, normal ) < Data.SlideMinAngleToConsiderASlope )
			return false;

		var downhill = normal.WithZ( 0f ).Normal;
		return Vector3.GetAngle( horizontal.Normal, downhill ) <= Data.SlideSlopeFacingAngleToActivate;
	}

	private void StartSlide( Vector3 velocity, float dashFrictionTime )
	{
		slideBuffered = false;
		StopEffect( ref dashEffect );
		StopEffect( ref slideEffect );
		var direction = velocity.WithZ( 0f ).Length > 1f ? velocity.WithZ( 0f ) : Rotation.FromYaw( bodyYaw ).Forward;
		slideEffect = PlayEffect( Data.SlideParticle, WorldPosition, Rotation.LookAt( direction ), true, values: Vector3.One );
		MoveState = HeroMoveState.Slide;
		slideTime = 0f;
		slideDashFrictionTime = dashFrictionTime;
		controller.Velocity = velocity.WithZ( 0f );
	}

	private void EndSlide( bool getUp )
	{
		StopEffect( ref slideEffect );
		MoveState = HeroMoveState.Normal;
		slideCooldown = Data.SlideCooldown;
		timeSinceSlideEnd = 0f;

		if ( getUp )
			Trigger( "slide_getup" );
	}

	private void UpdateCrouch()
	{
		var wantsCrouch = MoveState == HeroMoveState.Slide || (input.DuckHeld && MoveState == HeroMoveState.Normal);

		if ( wantsCrouch && !IsCrouching )
		{
			IsCrouching = true;
			controller.Height = CrouchingHeight;
		}
		else if ( !wantsCrouch && IsCrouching && CanStand() )
		{
			IsCrouching = false;
			controller.Height = StandingHeight;
		}
	}

	private bool CanStand()
	{
		var crouched = controller.BoundingBox;
		var position = WorldPosition;
		var trace = Scene.Trace.Box( crouched, position, position + Vector3.Up * (StandingHeight - CrouchingHeight) )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( PassableTag )
			.Run();

		return !trace.Hit;
	}

	private void Move( Vector3 wishDirection, float dt )
	{
		if ( MeleeLunging )
		{
			MoveMeleeLunge( dt );
			return;
		}

		switch ( MoveState )
		{
			case HeroMoveState.GroundDash:
			case HeroMoveState.AirDash:
				MoveDash( dt );
				break;

			case HeroMoveState.Slide:
				MoveSlide( dt );
				break;

			case HeroMoveState.Mantle:
				MoveMantle( dt );
				break;

			default:
				MoveNormal( wishDirection, dt );
				break;
		}
	}

	private void MoveNormal( Vector3 wishDirection, float dt )
	{
		var velocity = controller.Velocity;

		if ( controller.IsOnGround )
		{
			velocity = ApplyFriction( velocity.WithZ( 0f ), Friction, dt );
			velocity = Accelerate( velocity, wishDirection, GroundSpeed(), Data.MoveAcceleration, dt );

			// Running out of keys from a run plays the stop.
			var moving = wishDirection.Length > 0f;
			if ( wasMoving && !moving && velocity.Length > Data.MaxMoveSpeed * 0.5f )
				Trigger( "run_to_stop" );
			wasMoving = moving;
		}
		else
		{
			// In the air the hero keeps its momentum: a little drag, and air control that only adds a little speed
			// along the keys (enough to steer, or stop by pushing against the motion).
			var controlScale = dashJumpAirControlTime > 0f ? 1f + Data.DashJumpExtraAirControlPercent / 100f : 1f;
			velocity = ApplyAirDrag( velocity, 0f, input.Move, dt );
			velocity = AirAccelerateVelocity( velocity, wishDirection, GroundSpeed(), 0f, controlScale, 1f, dt );

			// Shortly after a wall jump, steering back toward the wall gets a fading bonus acceleration.
			var toward = wishDirection.Dot( -wallJumpNormal );
			if ( returnToWallTime > 0f && toward > 0f && Data.WallJumpReturnToWallBonusAccel > 0f )
				velocity -= wallJumpNormal * (Data.WallJumpReturnToWallBonusAccel * toward * (returnToWallTime / Data.WallJumpLateralInputSuppressTime) * dt);

			velocity -= Vector3.Up * Gravity * dt;
			wasMoving = false;

			if ( TryMantle( wishDirection, dt ) )
				return;
		}

		controller.Velocity = velocity;
		controller.Move();
	}

	/// <summary>
	/// Deadlock's ground acceleration (Source's): adds speed toward the wished direction, up to the wished speed along
	/// it, at the hero's acceleration times the wished speed a second, but at least times citadel_min_accel_speed.
	/// </summary>
	private static Vector3 Accelerate( Vector3 velocity, Vector3 wishDirection, float wishSpeed, float acceleration, float dt )
	{
		if ( wishDirection.Length <= 0f )
			return velocity;

		var add = wishSpeed - velocity.Dot( wishDirection );
		if ( add <= 0f )
			return velocity;

		return velocity + wishDirection * MathF.Min( acceleration * MathF.Max( MinAccelSpeed, wishSpeed ) * dt, add );
	}

	/// <summary>
	/// Deadlock's air acceleration: Source's, with the speed it adds along the keys capped at the air control speed
	/// (<see cref="AirControlSpeedSlow"/>, down to <see cref="AirControlSpeedFast"/> when going fast) or, while a dash
	/// raises it, the air speed max; scaled by the air control percent. How fast is sv_airaccelerate times the wished
	/// speed (and the acceleration scale) a second.
	/// </summary>
	private static Vector3 AirAccelerateVelocity( Vector3 velocity, Vector3 wishDirection, float wishSpeed, float airSpeedMax, float controlScale, float accelerationScale, float dt )
	{
		if ( wishDirection.Length <= 0f )
			return velocity;

		var cap = airSpeedMax > 0f
			? airSpeedMax
			: MathX.Lerp( AirControlSpeedSlow, AirControlSpeedFast, InverseLerp( AirControlSlowUntilSpeed, AirControlFastFromSpeed, velocity.WithZ( 0f ).Length ) );

		var add = MathF.Min( wishSpeed, cap * controlScale ) - velocity.Dot( wishDirection );
		if ( add <= 0f )
			return velocity;

		return velocity + wishDirection * MathF.Min( add, AirAccelerate * accelerationScale * wishSpeed * dt );
	}

	/// <summary>
	/// Deadlock's air drag, friction in the air: horizontal speed drops by max(stop speed, speed) times the drag a
	/// second. The drag is <see cref="AirDragMin"/>, or more while something adds some (<see cref="AirDragBonus"/>).
	/// While a dash raises the air speed max, only speed over it drags, and the axes (relative to the camera) the keys
	/// don't push along.
	/// </summary>
	private Vector3 ApplyAirDrag( Vector3 velocity, float airSpeedMax, Vector2 move, float dt )
	{
		var horizontal = velocity.WithZ( 0f );
		var speed = horizontal.Length;
		if ( speed < 0.1f )
			return Vector3.Up * velocity.z;

		var drag = MathF.Max( AirDragMin, AirDragBonus( velocity ) );
		var scale = MathF.Max( 0f, speed - MathF.Max( StopSpeed, speed ) * drag * dt ) / speed;

		var forward = Rotation.FromYaw( EyeAngles.yaw ).Forward;
		var forwardPart = forward * horizontal.Dot( forward );
		var sidePart = horizontal - forwardPart;

		if ( speed > airSpeedMax || (move.x == 0f && airSpeedMax > 0f) )
			forwardPart *= scale;

		if ( speed > airSpeedMax || (move.y == 0f && airSpeedMax > 0f) )
			sidePart *= scale;

		return (forwardPart + sidePart).WithZ( velocity.z );
	}

	/// <summary>
	/// Air drag added for a moment by the dash and the slide (their MODIFIER_VALUE_AIR_DRAG). Right after an air dash,
	/// over its post drag duration, a hero still going faster than the min drag speed drags up to the post max drag
	/// (more the faster, growing over that time). Slide jumps in a row drag from the second, fading out over the
	/// slide's air drag time.
	/// </summary>
	private float AirDragBonus( Vector3 velocity )
	{
		var drag = 0f;

		var postDrag = timeSinceAirDash / MathF.Max( Data.DashPostDragDuration, 0.001f );
		var speed = velocity.Length;
		if ( postDrag > 0f && postDrag < 1f && speed > Data.DashAirSpeedForMinDrag )
			drag += MathX.Lerp( 0f, Data.DashPostMaxDrag, InverseLerp( Data.DashAirSpeedForMinDrag, Data.DashAirSpeedForMaxDrag, speed ) ) * postDrag;

		if ( MoveState != HeroMoveState.Slide && slideJumps >= 2 && timeSinceSlideEnd <= Data.SlideAirDragAfterJumpTime )
			drag += MathX.Lerp( Data.SlideAirDragAfterJump, 0f, InverseLerp( 0f, Data.SlideAirDragAfterJumpTime, timeSinceSlideEnd ) );

		return drag;
	}

	/// <summary>
	/// Source style ground friction on the horizontal velocity.
	/// </summary>
	private Vector3 ApplyFriction( Vector3 velocity, float friction, float dt )
	{
		var horizontal = velocity.WithZ( 0f );
		var speed = horizontal.Length;
		if ( speed < 0.1f )
			return Vector3.Up * velocity.z;

		var drop = MathF.Max( speed, StopSpeed ) * friction * dt;
		return (horizontal * (MathF.Max( 0f, speed - drop ) / speed)).WithZ( velocity.z );
	}

	private void MoveDash( float dt )
	{
		if ( MoveState == HeroMoveState.AirDash )
		{
			MoveAirDash( dt );
			return;
		}

		var previous = dashTime;
		dashTime = MathF.Min( dashTime + dt, dashDuration );

		// The dash moves the hero along its position curve; ground dashes follow the ground and fall off ledges.
		var curve = Data.GroundDashCurve;
		var moved = dashDistance * (curve.Evaluate( dashTime / dashDuration ) - curve.Evaluate( previous / dashDuration ));
		var vertical = controller.IsOnGround ? 0f : controller.Velocity.z - Gravity * dt;
		controller.Velocity = (dashDirection * (moved / dt)).WithZ( vertical );
		controller.Move();

		if ( dashTime < dashDuration )
			return;

		// It ends keeping its last speed: friction takes it back down from there.
		MoveState = HeroMoveState.Normal;
		timeSinceDash = 0f;
		StopEffect( ref dashEffect );
	}

	/// <summary>
	/// An air dash: no gravity, vertical speed all but cancelled, and air control toward the dash with its cap raised
	/// to the dash speed (its distance over its duration), falling to the end velocity scale of it over the dash, and
	/// weaker acceleration (the air dash acceleration percent). It doesn't stop the hero when it ends: the momentum it
	/// built carries on. It ends after its duration or once it has covered its distance.
	/// </summary>
	private void MoveAirDash( float dt )
	{
		dashTime += dt;
		var fraction = Math.Clamp( dashTime / dashDuration, 0f, 1f );
		var airSpeedMax = dashDistance / dashDuration * MathX.Lerp( 1f, Data.AirDashEndVelocityScale, fraction );

		var velocity = controller.Velocity;
		velocity.z = MathF.Max( StopSpeed, velocity.z ) * dt * Data.AirDashDuringDrag;

		if ( controller.IsOnGround )
		{
			// Landed mid dash: it runs on toward the dash.
			velocity = ApplyFriction( velocity.WithZ( 0f ), Friction, dt );
			velocity = Accelerate( velocity, dashDirection, GroundSpeed(), Data.MoveAcceleration, dt );
		}
		else
		{
			// The game holds the keys toward the dash, relative to the camera.
			var camera = Rotation.FromYaw( EyeAngles.yaw );
			var move = new Vector2( dashDirection.Dot( camera.Forward ), dashDirection.Dot( camera.Left ) );
			velocity = ApplyAirDrag( velocity, airSpeedMax, move, dt );
			velocity = AirAccelerateVelocity( velocity, dashDirection, MathF.Max( GroundSpeed(), airSpeedMax ), airSpeedMax, 1f, 1f + Data.AirDashAccelerationPercent / 100f, dt );
		}

		controller.Velocity = velocity;
		controller.Move();

		if ( fraction >= 1f || (WorldPosition - airDashStart).WithZ( 0f ).Length >= dashDistance )
			EndAirDash();
	}

	private void EndAirDash()
	{
		MoveState = HeroMoveState.Normal;
		timeSinceDash = 0f;
		timeSinceAirDash = 0f;
		StopEffect( ref dashEffect );
	}

	private void MoveSlide( float dt )
	{
		slideTime += dt;
		slideDashFrictionTime -= dt;

		var horizontal = controller.Velocity.WithZ( 0f );
		var speed = horizontal.Length;
		var moveYaw = speed > 1f ? YawOf( horizontal ) : bodyYaw;

		// The camera steers the slide.
		var steer = DeltaAngle( moveYaw, EyeAngles.yaw );
		if ( MathF.Abs( steer ) > Data.SlideTurnMinAngle )
			moveYaw += Math.Clamp( steer, -Data.SlideTurnSpeed * dt, Data.SlideTurnSpeed * dt );

		var direction = Rotation.FromYaw( moveYaw ).Forward;
		var normal = GroundNormal();
		var slope = Vector3.GetAngle( Vector3.Up, normal );
		var downhill = normal.WithZ( 0f ).Length > 0.001f ? normal.WithZ( 0f ).Normal : Vector3.Zero;
		var steepness = InverseLerp( Data.SlideAccMinSlopeDeg, Data.SlideAccMaxSlopeDeg, slope );

		float friction;
		if ( slideDashFrictionTime > 0f )
			friction = Data.SlideDashFriction;
		else if ( slope < Data.SlideMinAngleToConsiderASlope )
			friction = slideTime < Data.SlideFlatGroundFrictionGraceTime ? Data.SlideFrictionFlatGroundGrace : Data.SlideFrictionFlatGround;
		else if ( direction.Dot( downhill ) >= 0f )
			friction = MathX.Lerp( Data.SlideFrictionMinSlope, Data.SlideFrictionMaxSlope, steepness );
		else
			friction = MathX.Lerp( Data.SlideFrictionUphillMinSlope, Data.SlideFrictionUphillMaxSlope, steepness );

		var velocity = direction * (speed * MathF.Max( 0f, 1f - friction * dt ));

		// Slopes speed the slide up, to a limit.
		if ( slope >= Data.SlideAccMinSlopeDeg && downhill.Length > 0f )
		{
			var acceleration = MathX.Lerp( Data.SlideMinSlopeAcceleration, Data.SlideMaxSlopeAcceleration, steepness ) * MathX.Lerp( Data.SlideAccMinSlopeScale, 1f, steepness );
			var limit = MathX.Lerp( Data.SlideMinSlopeMaxAccSpeed, Data.SlideMaxSlopeMaxAccSpeed, steepness );
			var before = velocity.Length;
			velocity += downhill * acceleration * dt;
			velocity = velocity.ClampLength( MathF.Max( before, limit ) );
		}

		controller.Velocity = velocity.WithZ( controller.IsOnGround ? 0f : controller.Velocity.z - Gravity * dt );
		controller.Move();

		if ( slideEffect.IsValid() && slideEffect.ControlPoints.Count > 2 && slideEffect.ControlPoints[2].IsValid() && velocity.Length > 1f )
			slideEffect.ControlPoints[2].WorldRotation = Rotation.LookAt( velocity.WithZ( 0f ) );

		if ( !controller.IsOnGround )
			EndSlide( false );
		else if ( !input.DuckHeld && slideTime >= Data.SlideMinDuration )
			EndSlide( true );
		else if ( controller.Velocity.WithZ( 0f ).Length < Data.SlideMinSustainSpeed )
			EndSlide( false );
	}

	private Vector3 GroundNormal()
	{
		var position = WorldPosition;
		var trace = Scene.Trace.Ray( position + Vector3.Up * 4f, position - Vector3.Up * 24f )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( PassableTag )
			.Run();

		return trace.Hit ? trace.Normal : Vector3.Up;
	}

	/// <summary>
	/// Starts a mantle when the hero, in the air, keeps pushing into a wall with a ledge it can reach: the game's
	/// mantle types (by ledge height) rise for a set time and then move over the ledge.
	/// </summary>
	private bool TryMantle( Vector3 wishDirection, float dt )
	{
		if ( wishDirection.Length <= 0f || !FindLedge( wishDirection, out var top ) )
		{
			mantlePushTime = 0f;
			return false;
		}

		mantlePushTime += dt;
		if ( mantlePushTime < Data.AutoMantlePushTime )
			return false;

		var position = WorldPosition;
		var height = top.z - position.z;
		var type = Data.MantleTypes
			.Where( x => height >= x.MinHeight && height <= x.MaxHeight )
			.OrderBy( x => MathF.Abs( x.AnimHeight - height ) )
			.FirstOrDefault();

		if ( type.AnimHeight == 0 )
			return false;

		MoveState = HeroMoveState.Mantle;
		mantleType = type;
		mantleStart = position;
		mantleRise = height + 1f;
		mantleDirection = (top - position).WithZ( 0f ).Normal;
		mantleForward = (top - position).WithZ( 0f ).Length;
		mantleTime = 0f;
		mantleSpeed = recentSpeed;
		mantlePushTime = 0f;
		controller.Velocity = Vector3.Zero;
		Trigger( $"mantle_{type.AnimHeight}" );
		return true;
	}

	/// <summary>
	/// A ledge in front of the hero it could stand on, with room to rise to it.
	/// </summary>
	private bool FindLedge( Vector3 direction, out Vector3 top )
	{
		top = default;

		var maxHeight = Data.MantleTypes.Max( x => x.MaxHeight );
		var hull = controller.BoundingBox;
		var position = WorldPosition;

		var wall = Scene.Trace.Box( hull, position, position + direction * 12f )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( PassableTag )
			.Run();

		if ( !wall.Hit || wall.StartedSolid || MathF.Abs( wall.Normal.z ) > 0.3f || direction.Dot( -wall.Normal ) < 0.5f )
			return false;

		// Room to rise straight up along the wall.
		var rise = Scene.Trace.Box( hull, wall.EndPosition, wall.EndPosition + Vector3.Up * (maxHeight + 1f) )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( PassableTag )
			.Run();

		var headroom = rise.Hit ? rise.EndPosition.z - wall.EndPosition.z : maxHeight + 1f;

		// Down onto the ledge, a hull's width past the wall.
		var over = wall.EndPosition - wall.Normal.WithZ( 0f ).Normal * (hull.Size.x + 4f);
		var down = Scene.Trace.Box( hull, over + Vector3.Up * headroom, over )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( PassableTag )
			.Run();

		if ( !down.Hit || down.StartedSolid || down.Normal.z < 0.7f )
			return false;

		var height = down.EndPosition.z - position.z;
		if ( height < 4f || height > maxHeight )
			return false;

		top = down.EndPosition;
		return true;
	}

	private void MoveMantle( float dt )
	{
		mantleTime += dt;

		Vector3 target;
		if ( mantleTime < mantleType.VerticalTime )
		{
			target = mantleStart + Vector3.Up * (mantleRise * mantleTime / mantleType.VerticalTime);
		}
		else
		{
			var t = Math.Clamp( (mantleTime - mantleType.VerticalTime) / MathF.Max( mantleType.HorizontalTime, 0.01f ), 0f, 1f );
			target = mantleStart + Vector3.Up * mantleRise + mantleDirection * (mantleForward * t);
		}

		controller.MoveTo( target, false );
		controller.Velocity = Vector3.Zero;

		if ( mantleTime < mantleType.VerticalTime + mantleType.HorizontalTime )
			return;

		// Over the ledge, the hero carries on at the speed it came at it with (and can slide on from there).
		MoveState = HeroMoveState.Normal;
		controller.IsOnGround = true;
		controller.Velocity = mantleDirection * mantleSpeed;
		jumpedSinceGrounded = false;
		timeSinceMantle = 0f;
	}

	/// <summary>
	/// How long after a mantle crouching still slides from the speed it carried over (like out of a dash).
	/// </summary>
	private const float MantleSlideWindow = 0.3f;

	private void UpdateFacing( Vector3 wishDirection, float dt )
	{
		var previous = bodyYaw;
		var aimYaw = EyeAngles.yaw;
		var horizontal = controller.Velocity.WithZ( 0f );

		switch ( MoveState )
		{
			case HeroMoveState.GroundDash:
				// One dash animation: the body turns to the dash.
				bodyYaw = ApproachAngle( bodyYaw, YawOf( dashDirection ), 1440f * dt );
				turnTime = 0f;
				break;

			case HeroMoveState.Slide:
				if ( horizontal.Length > 1f )
					bodyYaw = ApproachAngle( bodyYaw, YawOf( horizontal ), 1440f * dt );
				turnTime = 0f;
				break;

			case HeroMoveState.Normal when meleePhase is MeleePhase.Charging or MeleePhase.Attacking:
				bodyYaw = ApproachAngle( bodyYaw, YawOf( meleeDirection.Length > 0f ? meleeDirection : Rotation.FromYaw( aimYaw ).Forward ), 1440f * dt );
				turnTime = 0f;
				break;

			case HeroMoveState.Mantle:
				bodyYaw = ApproachAngle( bodyYaw, YawOf( mantleDirection ), 1440f * dt );
				turnTime = 0f;
				break;

			default:
				var moving = wishDirection.Length > 0f || horizontal.Length > 30f || !controller.IsOnGround;
				if ( moving || !animator.IsValid() || !animator.IsPlaying )
				{
					bodyYaw = ApproachAngle( bodyYaw, aimYaw, BodyTurnSpeed * dt );
					turnTime = 0f;
				}
				else if ( turnTime > 0f )
				{
					// The turn animation turns the body.
					turnTime -= dt;
					bodyYaw += animator.RootMotion.Rotation.Yaw();
				}
				else if ( MathF.Abs( DeltaAngle( bodyYaw, aimYaw ) ) > TurnInPlaceAngle )
				{
					Trigger( DeltaAngle( bodyYaw, aimYaw ) > 0f ? "turn_left_90" : "turn_right_90" );
					turnTime = 0.55f;
				}
				break;
		}

		bodyYaw = NormalizeAngle( bodyYaw );
		bodyYawRate = DeltaAngle( previous, bodyYaw ) / dt;
	}

	private void Animate( Vector3 wishDirection, float dt )
	{
		if ( !animator.IsValid() || !body.IsValid() )
			return;

		body.WorldRotation = Rotation.FromYaw( bodyYaw );

		var velocity = controller.Velocity;
		var horizontal = velocity.WithZ( 0f );
		var facing = Rotation.FromYaw( bodyYaw );
		var speed = horizontal.Length;
		var grounded = controller.IsOnGround;
		var moving = wishDirection.Length > 0f || speed > 30f;
		var crouched = IsCrouching || MoveState == HeroMoveState.Slide;

		crouchFraction = Approach( crouchFraction, crouched ? 1f : 0f, dt / 0.2f );

		animator.SetFloat( "forward_speed", horizontal.Dot( facing.Forward ) );
		animator.SetFloat( "strafe_speed", horizontal.Dot( facing.Left ) );
		animator.SetFloat( "move_speed", speed );
		animator.SetFloat( "vertical_speed", velocity.z );
		animator.SetFloat( "crouch_fraction", crouchFraction );
		animator.SetFloat( "look_heading", Math.Clamp( DeltaAngle( bodyYaw, EyeAngles.yaw ), -90f, 90f ) );
		animator.SetFloat( "look_pitch", Math.Clamp( -EyeAngles.pitch, -90f, 90f ) );
		animator.SetFloat( "turn_speed", bodyYawRate );
		animator.SetFloat( "input_forward", input.Move.x );
		animator.SetFloat( "input_right", -input.Move.y );

		animator.SetId( "movement_type", !grounded ? "in_air" : moving ? "running" : speed > 5f ? "run_to_stop" : "" );
		animator.SetId( "aim_set", !grounded ? "in_air"
			: MoveState == HeroMoveState.Slide ? "slide"
			: IsCrouching ? (moving ? "crouch_run" : "crouch")
			: moving ? "run" : "idle" );
		animator.SetId( "base_state", wallAttach.Length > 0 ? wallAttach : IsOutOfCombat ? "out_of_combat" : "" );
		animator.SetId( "hero_action_source", MoveState == HeroMoveState.Mantle ? "mantle" : "" );
		animator.SetId( "base_action", pendingAction ?? HeldAction() );
		pendingAction = null;

		animator.Step( dt );
	}

	/// <summary>
	/// Sets the graph's <c>base_action</c> for its next update. The game's graphs take actions (jumps, dashes,
	/// turns) for a single update and start them again while they stay set; states like sliding stay set.
	/// </summary>
	private void Trigger( string action ) => pendingAction = action;

	/// <summary>
	/// Melee, as the game's hold melee does it: a press released quickly swings a light attack (alternating the two
	/// light swings); held, a heavy attack charges (slowing the hero to the charge move speed) and strikes by itself
	/// once charged. Attacks lunge where the camera looks along their movement speed curve, then slow the hero over
	/// their speed bonus curve, and have a cooldown.
	/// </summary>
	private void UpdateMelee( float dt )
	{
		switch ( meleePhase )
		{
			case MeleePhase.None:
				if ( input.MeleePressed && meleeCooldown <= 0f && MoveState is HeroMoveState.Normal or HeroMoveState.Slide )
				{
					meleePhase = MeleePhase.Holding;
					meleeTime = 0f;
					combatTime = OutOfCombatDelay;
				}
				break;

			case MeleePhase.Holding:
				meleeTime += dt;

				if ( !input.MeleeHeld )
				{
					StartMeleeAttack( false );
				}
				else if ( meleeTime >= Data.LightMeleeMaxHoldTime )
				{
					meleePhase = MeleePhase.Charging;
					meleeTime = 0f;
					meleeDirection = MeleeAim( false );
					if ( MoveState == HeroMoveState.Slide )
						EndSlide( false );

					meleeChargeEffect = PlayEffect( Data.MeleeHoldParticle, WorldPosition, Rotation.FromYaw( EyeAngles.yaw ), true, attachment: "palm_L" );
				}
				break;

			case MeleePhase.Charging:
				meleeTime += dt;
				meleeDirection = MeleeAim( false );

				// The heavy attack strikes once melee has been held its required hold time (from the press).
				if ( meleeTime >= Data.HeavyMeleeRequiredHoldTime - Data.LightMeleeMaxHoldTime )
					StartMeleeAttack( true );
				break;

			case MeleePhase.Attacking:
				meleeTime += dt;

				// A heavy attack hits as its lunge ends.
				var lungeEnd = meleeAttack?.MovementSpeed?.End ?? 0f;
				if ( meleeHeavy && !meleeHit && meleeTime >= lungeEnd )
				{
					meleeHit = true;
					Trigger( "melee_heavy" );
					PlayEffect( meleeAttack?.SwingParticle, WorldPosition + Vector3.Up * (StandingHeight * 0.6f), Rotation.LookAt( meleeDirection ), false );
				}

				if ( meleeTime >= MathF.Max( meleeAttack?.StateTime ?? 0.5f, lungeEnd + 0.05f ) )
					meleePhase = MeleePhase.None;
				break;
		}
	}

	private void StartMeleeAttack( bool heavy )
	{
		meleeHeavy = heavy;
		meleeAir = !controller.IsOnGround;
		meleeAttack = heavy ? (meleeAir ? Data.HeavyAirMelee : Data.HeavyMelee) : Data.LightMelee;
		meleePhase = MeleePhase.Attacking;
		meleeTime = 0f;
		meleeHit = false;
		meleeDirection = MeleeAim( heavy && meleeAir );
		meleeCooldown = meleeAttack?.Cooldown ?? 0.75f;
		combatTime = OutOfCombatDelay;

		StopEffect( ref meleeChargeEffect );
		if ( MoveState == HeroMoveState.Slide )
			EndSlide( false );

		var chest = WorldPosition + Vector3.Up * (StandingHeight * 0.6f);
		var facing = Rotation.LookAt( meleeDirection );

		if ( heavy )
		{
			PlayEffect( meleeAttack?.AttackParticle, chest, facing, true );
		}
		else
		{
			Trigger( meleeSecondSwing ? "melee_light2" : "melee_light1" );
			meleeSecondSwing = !meleeSecondSwing;
			PlayEffect( meleeAttack?.SwingParticle, chest, facing, false );
		}
	}

	/// <summary>
	/// Where a melee goes: flat along the camera, or with an air heavy attack, along the camera's pitch too.
	/// </summary>
	private Vector3 MeleeAim( bool withPitch )
	{
		var rotation = withPitch ? EyeAngles.ToRotation() : Rotation.FromYaw( EyeAngles.yaw );
		return rotation.Forward;
	}

	private bool MeleeLunging => meleePhase == MeleePhase.Attacking && meleeAttack?.MovementSpeed is { } curve && meleeTime < curve.End;

	private void MoveMeleeLunge( float dt )
	{
		// The attack moves at its movement speed curve plus its speed bonus curve (negative: the heavy lunge starts
		// slower), then hands the hero back its own control.
		var bonus = meleeAttack.SpeedBonus is { } bonusCurve && meleeTime <= bonusCurve.End ? bonusCurve.Evaluate( meleeTime ) : 0f;
		var speed = MathF.Max( 0f, meleeAttack.MovementSpeed.Evaluate( meleeTime ) + bonus );
		var velocity = meleeDirection * speed;

		// Ground lunges follow the ground; an air heavy attack flies where it aims.
		if ( !(meleeHeavy && meleeAir) )
			velocity = velocity.WithZ( controller.IsOnGround ? 0f : controller.Velocity.z - Gravity * dt );

		controller.Velocity = velocity;
		controller.Move();

		// Out of the lunge, the hero keeps going at no more than its running speed.
		if ( meleeTime + dt >= meleeAttack.MovementSpeed.End )
			controller.Velocity = controller.Velocity.WithZ( 0f ).ClampLength( GroundSpeed() ).WithZ( controller.Velocity.z );
	}

	/// <summary>
	/// The graph's held <c>base_action</c>: a heavy melee's charge and lunge, or sliding.
	/// </summary>
	private string HeldAction()
	{
		if ( meleePhase == MeleePhase.Charging )
			return "melee_charging";

		if ( meleeHeavy && MeleeLunging )
			return meleeAir ? "melee_air_dashing" : "melee_ground_dashing";

		return MoveState == HeroMoveState.Slide ? "Sliding" : "";
	}

	private void FollowAttachment( MovementEffect playing )
	{
		if ( playing.Attachment is null || !playing.Body.IsValid() || !body.IsValid() )
			return;

		if ( body.GetAttachment( playing.Attachment ) is { } attachment )
			playing.Body.WorldTransform = attachment;
	}

	/// <summary>
	/// Plays one of the movement abilities' particle systems the way the game binds them: control point 0 at
	/// <paramref name="origin"/> (the feet), 1 at the body (or <paramref name="body"/>), 2 at the origin facing
	/// <paramref name="facing"/>, and 3 holding <paramref name="values"/>. With <paramref name="follow"/> they move
	/// with the hero; otherwise the effect stays where it started.
	/// </summary>
	private DeadlockParticleEffect PlayEffect( string path, Vector3 origin, Rotation facing, bool follow, Transform? body = null, Vector3? values = null, string attachment = null )
	{
		if ( DeadlockParticleSystem.Load( path ) is not { } system )
			return null;

		var root = new GameObject( true, "Effect" );
		root.WorldPosition = origin;
		root.WorldRotation = facing;
		if ( follow )
			root.SetParent( GameObject, true );

		var bodyPoint = new GameObject( true, "Body" );
		bodyPoint.SetParent( root, false );
		bodyPoint.WorldTransform = body ?? new Transform( origin + Vector3.Up * (StandingHeight * 0.55f), facing );

		var facingPoint = new GameObject( true, "Facing" );
		facingPoint.SetParent( root, false );
		facingPoint.WorldRotation = facing;

		var effect = root.Components.Create<DeadlockParticleEffect>();
		effect.ControlPoints = [root, bodyPoint, facingPoint];
		if ( values is { } value )
			effect.ControlPointValues = [new DeadlockControlPointValue { ControlPoint = 3, Value = value }];
		effect.Particle = system;

		movementEffects.Add( new MovementEffect { Effect = effect, Attachment = attachment, Body = bodyPoint } );
		FollowAttachment( movementEffects[^1] );
		return effect;
	}

	/// <summary>
	/// Stops a following effect emitting; what it already emitted plays out where it is.
	/// </summary>
	private static void StopEffect( ref DeadlockParticleEffect effect )
	{
		if ( effect.IsValid() )
		{
			effect.StopEmission();
			effect.GameObject.SetParent( null, true );
		}

		effect = null;
	}

	/// <summary>
	/// Removes effects that have played out.
	/// </summary>
	private void UpdateEffects( float dt )
	{
		for ( var i = movementEffects.Count - 1; i >= 0; i-- )
		{
			var playing = movementEffects[i];
			playing.Age += dt;
			FollowAttachment( playing );

			if ( !playing.Effect.IsValid() )
			{
				movementEffects.RemoveAt( i );
				continue;
			}

			// Finished (it gets a moment to start), or going on far longer than any of them should.
			if ( (playing.Age > 0.5f && !playing.Effect.IsPlaying && playing.Effect != slideEffect && playing.Effect != dashEffect && playing.Effect != meleeChargeEffect) || playing.Age > 30f )
			{
				playing.Effect.GameObject.Destroy();
				movementEffects.RemoveAt( i );
			}
		}
	}

	/// <summary>
	/// A crosshair and the stamina pips under it, as the game shows them: one per stamina point, filling up as it
	/// regenerates.
	/// </summary>
	private void DrawHud()
	{
		var camera = Camera ?? Scene.Camera;
		if ( !ShowHud || !camera.IsValid() || Data is null )
			return;

		using var hud = camera.BeginHud();
		var center = Screen.Size * 0.5f;

		hud.Fill = Color.White.WithAlpha( 0.9f );
		hud.Circle( center, 2f );

		var count = Math.Max( 1, (int)MathF.Round( Data.Stamina ) );
		const float width = 26f;
		const float height = 5f;
		const float gap = 5f;
		var left = center.x - (count * width + (count - 1) * gap) * 0.5f;
		var top = center.y + 34f;

		for ( var i = 0; i < count; i++ )
		{
			var x = left + i * (width + gap);
			var fill = Math.Clamp( Stamina - i, 0f, 1f );

			hud.Fill = Color.Black.WithAlpha( 0.5f );
			hud.Rect( new Rect( x, top, width, height ), 2f );

			if ( fill > 0f )
			{
				hud.Fill = fill >= 1f ? Color.White : Color.White.WithAlpha( 0.45f );
				hud.Rect( new Rect( x, top, width * fill, height ), 2f );
			}
		}
	}

	private void UpdateCamera()
	{
		var camera = Camera ?? Scene.Camera;
		if ( !camera.IsValid() )
			return;

		var rotation = EyeAngles.ToRotation();
		var pivot = WorldPosition + Vector3.Up * MathX.Lerp( CameraHeight, CameraCrouchingHeight, crouchFraction );

		var shoulder = Scene.Trace.Ray( pivot, pivot + rotation.Right * CameraShoulder )
			.Radius( 8f )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( PassableTag )
			.Run();

		var back = Scene.Trace.Ray( shoulder.EndPosition, shoulder.EndPosition + rotation.Backward * CameraDistance )
			.Radius( 8f )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( PassableTag )
			.Run();

		camera.WorldPosition = back.EndPosition;
		camera.WorldRotation = rotation;
		camera.FieldOfView = CameraFieldOfView;
	}

	private static float YawOf( Vector3 direction ) => MathF.Atan2( direction.y, direction.x ) * (180f / MathF.PI);

	private static float NormalizeAngle( float angle )
	{
		angle %= 360f;
		return angle > 180f ? angle - 360f : angle < -180f ? angle + 360f : angle;
	}

	/// <summary>
	/// The signed shortest turn from one angle to another, positive to the left.
	/// </summary>
	private static float DeltaAngle( float from, float to ) => NormalizeAngle( to - from );

	private static float ApproachAngle( float from, float to, float step )
	{
		var delta = DeltaAngle( from, to );
		return from + Math.Clamp( delta, -step, step );
	}

	private static float Approach( float from, float to, float step )
		=> from < to ? MathF.Min( from + step, to ) : MathF.Max( from - step, to );

	private static float InverseLerp( float a, float b, float value )
		=> b == a ? 0f : Math.Clamp( (value - a) / (b - a), 0f, 1f );
}
