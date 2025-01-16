using DOL.GS.PlayerClass;

namespace DOL.GS.Scripts
{
    //public class MimicShaman : MimicNPC
    //{
    //    public MimicShaman(byte level) : base(new ClassShaman(), level)
    //    { }
    //}

    public class ShamanSpec : MimicSpec
    {
        public ShamanSpec(eSpecType spec)
        {
            SpecName = "ShamanSpec";

            WeaponOneType = eObjectType.Hammer;
            Is2H = Util.RandomBool();

            var randVariance = spec switch
            {
                eSpecType.AugShaman => Util.Random(0, 4),
                eSpecType.SubtShaman => Util.Random(5, 6),
                eSpecType.MendShaman => 7,
                _ => Util.Random(7),
            };
            
            switch (randVariance)
            {
                case 0:
                case 1:
                SpecType = eSpecType.AugShaman;
                Add(Specs.Mending, 8, 0.2f);
                Add(Specs.Augmentation, 46, 0.8f);
                Add(Specs.Subterranean, 27, 0.5f);
                break;

                case 2:
                SpecType = eSpecType.AugShaman;
                Add(Specs.Mending, 26, 0.5f);
                Add(Specs.Augmentation, 47, 0.8f);
                Add(Specs.Subterranean, 5, 0.0f);
                break;

                case 3:
                SpecType = eSpecType.AugShaman;
                Add(Specs.Mending, 33, 0.5f);
                Add(Specs.Augmentation, 42, 0.8f);
                Add(Specs.Subterranean, 7, 0.0f);
                break;

                case 4:
                SpecType = eSpecType.AugShaman;
                Add(Specs.Mending, 21, 0.5f);
                Add(Specs.Augmentation, 48, 0.8f);
                Add(Specs.Subterranean, 12, 0.0f);
                break;

                case 5:
                SpecType = eSpecType.SubtShaman;
                Add(Specs.Mending, 4, 0.2f);
                Add(Specs.Augmentation, 28, 0.5f);
                Add(Specs.Subterranean, 46, 0.8f);
                break;

                case 6:
                SpecType = eSpecType.SubtShaman;
                Add(Specs.Mending, 27, 0.5f);
                Add(Specs.Augmentation, 8, 0.1f);
                Add(Specs.Subterranean, 46, 0.8f);
                break;

                case 7:
                SpecType = eSpecType.MendShaman;
                Add(Specs.Mending, 43, 0.8f);
                Add(Specs.Augmentation, 32, 0.5f);
                Add(Specs.Subterranean, 6, 0.0f);
                break;
            }
        }
    }
}