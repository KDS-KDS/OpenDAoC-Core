using DOL.GS.PlayerClass;

namespace DOL.GS.Scripts
{
    //public class MimicThane : MimicNPC
    //{
    //    public MimicThane(byte level) : base(new ClassThane(), level)
    //    { }
    //}

    public class ThaneSpec : MimicSpec
    {
        public ThaneSpec(eSpecType spec)
        {
            SpecName = "ThaneSpec";

            int randBaseWeap = Util.Random(2);

            switch (randBaseWeap)
            {
                case 0: WeaponOneType = eObjectType.Sword; break;
                case 1: WeaponOneType = eObjectType.Axe; break;
                case 2: WeaponOneType = eObjectType.Hammer; break;
            }

            var randVariance = spec switch
            {
                eSpecType.OneHanded => Util.Random(0, 2),
                eSpecType.OneHandAndShield => Util.Random(0,2),
                eSpecType.OneHandHybrid => Util.Random(0, 2),
                eSpecType.TwoHanded => 3,
                _ => Util.Random(3),
            };

            SpecType = eSpecType.Mid;

            switch (randVariance)
            {
                case 0:
                case 1:
                Add(ObjToSpec(WeaponOneType), 39, 0.8f);
                Add(Specs.Stormcalling, 50, 1.0f);
                Add(Specs.Shields, 42, 0.5f);
                Add(Specs.Parry, 6, 0.0f);
                break;

                case 2:
                Add(ObjToSpec(WeaponOneType), 44, 0.8f);
                Add(Specs.Stormcalling, 48, 1.0f);
                Add(Specs.Shields, 35, 0.5f);
                Add(Specs.Parry, 18, 0.0f);
                break;

                case 3:
                Add(ObjToSpec(WeaponOneType), 50, 0.8f);
                Add(Specs.Stormcalling, 50, 1.0f);
                Add(Specs.Parry, 28, 0.1f);
                break;
            }
        }
    }
}